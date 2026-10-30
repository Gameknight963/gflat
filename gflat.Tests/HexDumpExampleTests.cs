using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace gflat.Tests;

public sealed class HexDumpExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualToolMatchesBinaryReferenceWithoutStringOrManagedAllocations(int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("hexdump.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples/hexdump.gf"))))
            .Append(new SourceFile("no-allocations.gf", """
                replace void*? Memory::Allocate(nuint size) => null;
                replace void Memory::Free(void* storage) nothrow {}
                """));
        string directory = Path.Combine(Path.GetTempPath(), "gflat-hexdump-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "hexdump.ll"), executable = Path.Combine(directory, "hexdump.exe");
            File.WriteAllText(input, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compiled = NativeToolchain.Compile(input, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            foreach (int length in new[] { 0, 1, 15, 16, 17, 256, 4095, 4096, 4097, 12345 })
            {
                byte[] bytes = Enumerable.Range(0, length).Select(i => (byte)(i * 37 + 11)).ToArray();
                string path = Path.Combine(directory, $"input {length}.bin");
                File.WriteAllBytes(path, bytes);
                var result = NativeToolchain.Run(executable, [path], 30000);
                Assert.Equal(0, result.ExitCode);
                Assert.Empty(result.StandardError);
                Assert.Equal(Reference(bytes), result.StandardOutput.Replace("\r\n", "\n"));
                foreach (int skip in new[] { 0, 1, 15, 16, 17, 4095, 4096, 4097, 8192, length }
                    .Where(count => count <= length).Distinct())
                {
                    foreach (string count in new[] { skip.ToString(CultureInfo.InvariantCulture), "+00" + skip.ToString(CultureInfo.InvariantCulture) })
                    {
                        var skipped = NativeToolchain.Run(executable, ["--skip", count, path], 30000);
                        Assert.Equal(0, skipped.ExitCode);
                        Assert.Empty(skipped.StandardError);
                        Assert.Equal(Reference(bytes, skip), skipped.StandardOutput.Replace("\r\n", "\n"));
                        Assert.Equal(bytes, File.ReadAllBytes(path));
                    }
                }
                string maximum = UIntPtr.Size == 8 ? ulong.MaxValue.ToString(CultureInfo.InvariantCulture) : uint.MaxValue.ToString(CultureInfo.InvariantCulture);
                foreach (string count in new[] { (length + 1).ToString(CultureInfo.InvariantCulture), maximum })
                {
                    var truncated = NativeToolchain.Run(executable, ["--skip", count, path], 30000);
                    Assert.Equal(1, truncated.ExitCode);
                    Assert.Empty(truncated.StandardOutput);
                    Assert.Equal($"Unable to dump: {path}\n", truncated.StandardError.Replace("\r\n", "\n"));
                    Assert.Equal(bytes, File.ReadAllBytes(path));
                }
            }
            string missing = Path.Combine(directory, "missing.bin");
            foreach (string[] arguments in new[] { new[] { missing }, new[] { "--skip", "0", missing } })
            {
                var failure = NativeToolchain.Run(executable, arguments, 30000);
                Assert.Equal(1, failure.ExitCode);
                Assert.Empty(failure.StandardOutput);
                Assert.Equal($"Unable to dump: {missing}\n", failure.StandardError.Replace("\r\n", "\n"));
            }
            List<string[]> invalidArguments = [[], ["a", "b"], ["--skip", "0"], ["--skip", "0", missing, "extra"], ["--other", "0", missing]];
            string overflow = UIntPtr.Size == 8 ? "18446744073709551616" : "4294967296";
            foreach (string count in new[] { "", "-0", "-1", "+", " 1", "1 ", "0x10", "1x", "1.0", overflow, "99999999999999999999999999999999999999" })
                invalidArguments.Add(["--skip", count, missing]);
            foreach (string[] arguments in invalidArguments)
            {
                var usage = NativeToolchain.Run(executable, arguments, 30000);
                Assert.Equal(2, usage.ExitCode);
                Assert.Empty(usage.StandardOutput);
                Assert.Equal("Usage: hexdump [--skip <count>] <file>\n", usage.StandardError.Replace("\r\n", "\n"));
            }
            byte[] literal = [0, 32, 65, 126, 127, 255];
            File.WriteAllBytes(Path.Combine(directory, "--skip"), literal);
            var literalPath = await RunInDirectory(executable, directory, ["--skip"]);
            Assert.Equal(0, literalPath.ExitCode);
            Assert.Empty(literalPath.StandardError);
            Assert.Equal(Reference(literal), literalPath.StandardOutput.Replace("\r\n", "\n"));
            Assert.Equal(literal, File.ReadAllBytes(Path.Combine(directory, "--skip")));
        }
        catch (Exception error)
        {
            // Preserve the original failure and its fixture for investigation.
            error.Data["HexDumpExampleDirectory"] = directory;
            throw;
        }
        await TestDirectoryCleanup.DeleteAsync(directory);
    }

    private static async Task<ProcessResult> RunInDirectory(string executable, string directory, string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); Assert.Fail("Hexdump timed out."); }
        return new ProcessResult(process.ExitCode, await output, await error);
    }

    private static string Reference(byte[] bytes, int skip = 0)
    {
        var output = new StringBuilder();
        for (int start = skip; start < bytes.Length; start += 16)
        {
            output.Append(start.ToString("x16")).Append("  ");
            int length = Math.Min(16, bytes.Length - start);
            for (int column = 0; column < 16; column++)
                output.Append(column < length ? bytes[start + column].ToString("x2") + " " : "   ");
            output.Append(" |");
            for (int column = 0; column < 16; column++)
            {
                if (column >= length) output.Append(' ');
                else output.Append(bytes[start + column] is >= 32 and <= 126 ? (char)bytes[start + column] : '.');
            }
            output.Append("|\n");
        }
        return output.ToString();
    }
}
