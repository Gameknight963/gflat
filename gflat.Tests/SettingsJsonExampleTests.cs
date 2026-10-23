using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace gflat.Tests;

public sealed class SettingsJsonExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualToolOwnsAcceptedTextAndRejectsInvalidInputBeforeOutput(int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("settings-json.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples/settings-json.gf"))));
        string directory = Path.Combine(Path.GetTempPath(), "gflat_settings_json_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string ir = Path.Combine(directory, "main.ll"), executable = Path.Combine(directory, "settings-json.exe");
            File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var valid = new List<(string Input, Dictionary<string, string> Expected)>
            {
                ("", new()),
                (" \t\r\n# ignored\n\t # ignored = value\n", new()),
                (" title = a=b=c \r\n empty =\n case = lower\nCase=upper", new()
                    { ["title"] = "a=b=c", ["empty"] = "", ["case"] = "lower", ["Case"] = "upper" }),
                ("emoji=😀\n日本語= café\n", new() { ["emoji"] = "😀", ["日本語"] = "café" }),
                ("a\0b = c\0d\nquote\" = a\\b\t# data\n", new()
                    { ["a\0b"] = "c\0d", ["quote\""] = "a\\b\t# data" }),
                ("x=" + new string('a', 4093), new() { ["x"] = new string('a', 4093) }),
                (string.Join("\n", Enumerable.Range(0, 180).Select(i => $"key{i}=value{i}")),
                    Enumerable.Range(0, 180).ToDictionary(i => $"key{i}", i => $"value{i}"))
            };
            foreach (var sample in valid)
            {
                var result = await Run(executable, Encoding.UTF8.GetBytes(sample.Input));
                Assert.True(result.Code == 0, result.Error);
                Assert.Empty(result.Error);
                Assert.EndsWith(Environment.NewLine, result.Output);
                using JsonDocument document = JsonDocument.Parse(result.Output);
                var properties = document.RootElement.EnumerateObject().ToList();
                Assert.Equal(sample.Expected.Count, properties.Count);
                foreach (var property in properties)
                {
                    Assert.True(sample.Expected.TryGetValue(property.Name, out string? expected), property.Name);
                    Assert.Equal(expected, property.Value.GetString());
                }
            }
            var invalid = new List<(byte[] Input, string Error)>
            {
                ("ok=yes\n# comment\nbad\n"u8.ToArray(), "Line 3: Expected key=value"),
                (" \t = value\n"u8.ToArray(), "Line 1: Key must not be empty"),
                ("x=first\nx=second\n"u8.ToArray(), "Line 2: Duplicate key"),
                ([107, 61, 255, 10], "Line 1: Expected valid UTF-8"),
                ([0xc0, 0x80, 61, 120, 10], "Line 1: Expected valid UTF-8"),
                (Encoding.ASCII.GetBytes("ok=yes\nx=" + new string('a', 4094) + "\n"),
                    "Line 2: Line exceeds the 4095-byte payload limit")
            };
            foreach (var sample in invalid)
            {
                var result = await Run(executable, sample.Input);
                Assert.Equal(2, result.Code);
                Assert.Empty(result.Output);
                Assert.Equal(sample.Error + Environment.NewLine, result.Error);
            }
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    private static async Task<(int Code, string Output, string Error)> Run(string executable, byte[] input)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        } };
        Assert.True(process.Start());
        Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.StandardInput.BaseStream.WriteAsync(input, timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            await process.WaitForExitAsync();
            throw new TimeoutException("settings-json timed out.");
        }
        return (process.ExitCode, await output, await error);
    }
}
