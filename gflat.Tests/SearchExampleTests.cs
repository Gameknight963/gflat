using System.Diagnostics;
using System.Text;

namespace gflat.Tests;

public sealed class SearchExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualToolMatchesCountedLinesAcrossChunksAndPreservesInput(int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("search.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples/search.gf"))));
        string directory = Path.Combine(Path.GetTempPath(), "gflat-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string ir = Path.Combine(directory, "main.ll"), executable = Path.Combine(directory, "search.exe"), input = Path.Combine(directory, "input file.bin");
            File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            (byte[] Input, string Pattern)[] cases =
            [
                ([], ""), (Bytes("\n\n"), ""), (Bytes("\n\n"), "needle"),
                (Bytes("match\nMATCH\r\nno\nmatchtail"), "match"),
                (Bytes("bare\rreturn\nfinal\r"), ""),
                (Bytes("needle" + new string('x', 4089) + "\r\nneedle tail"), "needle"),
                (Bytes(new string('a', 4094) + "needle" + new string('b', 10000) + "\nskip"), "needle"),
                (Bytes(new string('a', 4095) + "\nneedle\n"), "needle"),
                (Bytes("keep\0payload\nskip\0keep\nmiss\0value\n"), "keep"),
                ([255, 128, 0, .. Bytes("needle\r\n"), 254, .. Bytes("miss\n")], "needle"),
                (Bytes("prefix\nneedle\nlast\n"), "absent"),
                (Bytes("prefix\nneedle\nlast\n"), ""),
                (Bytes(new string('a', 65536) + "\n"), new string('a', 2046) + "ba")
            ];
            foreach (var item in cases)
            {
                File.WriteAllBytes(input, item.Input);
                var result = await Run(executable, [input, item.Pattern]);
                Assert.Equal(0, result.ExitCode); Assert.Empty(result.Error);
                Assert.Equal(Expected(item.Input, Bytes(item.Pattern)), result.Output);
                Assert.Equal(item.Input, File.ReadAllBytes(input));
            }
            foreach (string[] arguments in new[] { Array.Empty<string>(), [input], new[] { input, "needle", "extra" } })
            {
                var usage = await Run(executable, arguments);
                Assert.Equal(2, usage.ExitCode); Assert.Empty(usage.Error);
                Assert.Contains("Usage: search", Encoding.ASCII.GetString(usage.Output));
            }
            var missing = await Run(executable, [Path.Combine(directory, "missing file"), "needle"]);
            Assert.Equal(1, missing.ExitCode); Assert.Empty(missing.Error);
            Assert.Contains("Unable to read file.", Encoding.ASCII.GetString(missing.Output));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);

    private static byte[] Expected(byte[] input, byte[] pattern)
    {
        var result = new List<byte>();
        int start = 0;
        for (int end = 0; end < input.Length; end++)
        {
            if (input[end] != 10) continue;
            int length = end - start;
            if (length != 0 && input[end - 1] == 13) length--;
            Add(input.AsSpan(start, length));
            start = end + 1;
        }
        if (start < input.Length) Add(input.AsSpan(start));
        return result.ToArray();

        void Add(ReadOnlySpan<byte> line)
        {
            if (line.IndexOf(pattern) < 0) return;
            result.AddRange(line.ToArray());
            result.AddRange(Bytes(Environment.NewLine));
        }
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
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); Assert.Fail("search timed out."); }
        await bytes;
        return (process.ExitCode, output.ToArray(), await error);
    }
}
