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
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
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
            (byte[] Input, string Pattern)[] foldedCases =
            [
                ([], ""), (Bytes("\n\n"), ""), (Bytes("\n\n"), "needle"),
                (Bytes("match\nMATCH\r\nno\nMaTcHtail"), "mAtCh"),
                (Bytes("before\0PAYLOAD\nnomatch\n"), "payload"),
                (Bytes("bare\rReturn\nfinal\r"), "return"),
                (Bytes(new string('x', 4094) + "NeEdLe\r\nlastNEEDLE"), "needle"),
                ([255, 128, 0, .. Bytes("NeEdLe\n"), 254, .. Bytes("miss\n")], "needle"),
                (Bytes("\u00e9\n\u00c9\nE\n"), "e"),
                (Bytes("prefix\nlast\r"), ""), (Bytes("aaaA\n"), "AaA")
            ];
            foreach (var item in foldedCases)
            {
                File.WriteAllBytes(input, item.Input);
                var result = await Run(executable, ["--ignore-case", input, item.Pattern]);
                Assert.Equal(0, result.ExitCode); Assert.Empty(result.Error);
                Assert.Equal(Expected(item.Input, Bytes(item.Pattern), ignoreCase: true), result.Output);
                Assert.Equal(item.Input, File.ReadAllBytes(input));
            }
            // The flag spelling remains a valid file operand in the original two-operand mode.
            File.WriteAllBytes(Path.Combine(directory, "--ignore-case"), Bytes("needle\nNEEDLE\n"));
            var literalName = await Run(executable, ["--ignore-case", "needle"], directory);
            Assert.Equal(0, literalName.ExitCode); Assert.Empty(literalName.Error);
            Assert.Equal(Bytes("needle" + Environment.NewLine), literalName.Output);
            foreach (string[] arguments in new[] { Array.Empty<string>(), [input], new[] { input, "needle", "extra" }, new[] { "--unknown", input, "needle" } })
            {
                var usage = await Run(executable, arguments);
                Assert.Equal(2, usage.ExitCode); Assert.Empty(usage.Output);
                Assert.Equal("Usage: search [--ignore-case] <file> <text>", usage.Error.TrimEnd('\r','\n'));
            }
            var missing = await Run(executable, [Path.Combine(directory, "missing file"), "needle"]);
            Assert.Equal(1, missing.ExitCode); Assert.Empty(missing.Output);
            Assert.Equal("Unable to read file.", missing.Error.TrimEnd('\r','\n'));
        }
        catch (Exception error) { error.Data["SearchExampleDirectory"] = directory; throw; }
        Directory.Delete(directory, true);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ActualToolReportsImmediateAndBufferedOutputFailure(int optimization)
        => ExampleOutputFailure.AssertReported("search", optimization, "Unable to write search results.", "needle");

    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);

    private static byte[] Expected(byte[] input, byte[] pattern, bool ignoreCase = false)
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
            byte[] comparable = line.ToArray();
            byte[] sought = pattern;
            if (ignoreCase)
            {
                comparable = comparable.Select(FoldAscii).ToArray();
                sought = pattern.Select(FoldAscii).ToArray();
            }
            if (comparable.AsSpan().IndexOf(sought) < 0) return;
            result.AddRange(line.ToArray());
            result.AddRange(Bytes(Environment.NewLine));
        }
    }

    private static byte FoldAscii(byte value)
        => value is >= (byte)'a' and <= (byte)'z' ? (byte)(value - 32) : value;

    private static async Task<(int ExitCode, byte[] Output, string Error)> Run(string executable, string[] arguments, string? workingDirectory = null)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        if (workingDirectory != null) start.WorkingDirectory = workingDirectory;
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
