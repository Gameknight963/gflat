using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace gflat.Tests;

public sealed class UnquoteLinesExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualToolDecodesCompleteStringsInPlaceAndReportsInvalidLines(int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("unquote-lines.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples/unquote-lines.gf"))));
        string directory = Path.Combine(Path.GetTempPath(), "gflat-unquote-lines-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string ir = Path.Combine(directory, "main.ll"), executable = Path.Combine(directory, "unquote-lines.exe"), input = Path.Combine(directory, "input strings.jsonl");
            File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var serializerOptions = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            string[][] cases = [[], [""], ["a", "", "last"], ["\"quoted\"\\path\t\0\b\f", "line\nfeed\rreturn"],
                ["\u20ac\ud83d\ude00\uffff", "\ufeff"], [new string('x', 10000), "short", ""],
                Enumerable.Range(250, 15).Select(n => new string('a', n) + "\0\u20ac").ToArray()];
            foreach (var values in cases)
            {
                foreach (string separator in new[] { "\n", "\r\n" })
                {
                    string encoded = string.Join(separator, values.Select(value => JsonSerializer.Serialize(value, serializerOptions)));
                    if (values.Length != 0) encoded += separator;
                    byte[] original = Encoding.UTF8.GetBytes(encoded);
                    File.WriteAllBytes(input, original);
                    var result = await Run(executable, [input]);
                    Assert.Equal(0, result.ExitCode);
                    Assert.Empty(result.Error);
                    string expected = string.Concat(values.Select(value => value + "\n"));
                    // Console deliberately retains the CRT's host text mode.
                    if (OperatingSystem.IsWindows()) expected = expected.Replace("\n", "\r\n");
                    Assert.Equal(Encoding.UTF8.GetBytes(expected), result.Output);
                    Assert.Equal(original, File.ReadAllBytes(input));
                    foreach (string literal in encoded.Split(separator, StringSplitOptions.RemoveEmptyEntries))
                    {
                        using var document = JsonDocument.Parse(literal);
                        Assert.Contains(document.RootElement.GetString(), values);
                    }
                }
            }
            // Unterminated final line; explicit surrogate pair and standard escapes.
            File.WriteAllText(input, "\"\\uD83D\\uDE00\\/\\n\\u0000\"");
            var escaped = await Run(executable, [input]);
            Assert.Equal(0, escaped.ExitCode);
            Assert.Equal(Encoding.UTF8.GetBytes(OperatingSystem.IsWindows() ? "\ud83d\ude00/\r\n\0\r\n" : "\ud83d\ude00/\n\0\n"), escaped.Output);

            string[] invalid = ["", "x", "null", "123", "[]", "\"a\" \"b\"", " \"a\"", "\"a\" ", "\"unterminated", "\"\\x\"", "\"\\uD800\"", "\"\\uDC00\"", "\"raw\tcontrol\"", "\ufeff\"bom\""];
            foreach (string bad in invalid)
            {
                byte[] original = Encoding.UTF8.GetBytes("\"first\"\n" + bad + "\n\"not emitted\"\n");
                File.WriteAllBytes(input, original);
                var result = await Run(executable, [input]);
                Assert.Equal(1, result.ExitCode);
                Assert.Equal(Encoding.UTF8.GetBytes(OperatingSystem.IsWindows() ? "first\r\n" : "first\n"), result.Output);
                Assert.Contains("line 2", result.Error);
                Assert.Contains(input, result.Error);
                Assert.Contains("JSON string", result.Error);
                Assert.Equal(original, File.ReadAllBytes(input));
            }
            foreach (byte[] invalidUtf8 in new byte[][] { [34, 0xc0, 0x80, 34], [34, 0xed, 0xa0, 0x80, 34], [34, 0xf0, 0x90, 0x80, 34], [34, 0xff, 34] })
            {
                File.WriteAllBytes(input, invalidUtf8);
                var result = await Run(executable, [input]);
                Assert.Equal(1, result.ExitCode);
                Assert.Empty(result.Output);
                Assert.Contains("line 1", result.Error);
                Assert.Equal(invalidUtf8, File.ReadAllBytes(input));
            }
            var missing = await Run(executable, [Path.Combine(directory, "missing")]);
            Assert.Equal(1, missing.ExitCode); Assert.Empty(missing.Output); Assert.NotEmpty(missing.Error);
            Assert.Contains(Path.Combine(directory, "missing"), missing.Error);
            Assert.DoesNotContain("line 0", missing.Error);
            foreach (string[] arguments in new string[][] { [], [input, input] })
            {
                var usage = await Run(executable, arguments);
                Assert.Equal(2, usage.ExitCode); Assert.Empty(usage.Output); Assert.Contains("Usage:", usage.Error);
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task<(int ExitCode, byte[] Output, string Error)> Run(string executable, string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        using var output = new MemoryStream();
        var bytes = process.StandardOutput.BaseStream.CopyToAsync(output);
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); Assert.Fail("unquote-lines timed out."); }
        await bytes;
        return (process.ExitCode, output.ToArray(), await error);
    }
}
