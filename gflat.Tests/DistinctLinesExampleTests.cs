using System.Diagnostics;
using System.Text;

namespace gflat.Tests;

public sealed class DistinctLinesExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualToolKeepsFirstCountedLinesInInputOrder(int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("distinct-lines.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples/distinct-lines.gf"))));
        string directory = Path.Combine(Path.GetTempPath(), "gflat_distinct_lines_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string ir = Path.Combine(directory, "main.ll"), executable = Path.Combine(directory, "distinct-lines.exe");
            File.WriteAllText(ir, Compiler.Emit(sources, options: new() { NoStdLib = true, InternalizeFunctions = true }));
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            List<byte[]> cases =
            [
                [], [10], [10, 10, 10], [13, 10, 13, 10], [13],
                "one"u8.ToArray(), "one\n"u8.ToArray(), "one\none"u8.ToArray(),
                "pear\napple\npear\n\napple\n"u8.ToArray(),
                "same\nsame\r\nsame\n"u8.ToArray(),
                " beta\r\nAlpha\nbeta\r\n\nalpha\nAlpha"u8.ToArray(),
                [97,0,98,10,97,0,98,10,97,10,97,0,10,97,0,10,255,128,0,10,255,128,0,10,255,128,10,65,10,97],
                "b\rc\na\r\r\nbare\r"u8.ToArray(),
                [97,13,13,10,97,0,10,97,13,10],
                [122,10,97,98,26,105,103,110,111,114,101,100,10,97,10],
                [26,110,101,118,101,114,10],
                [122,13,10,97,98,13,26,105,103,110,111,114,101,100,10]
            ];
            byte[] longLine = Enumerable.Repeat((byte)'x', 18000).ToArray();
            cases.Add(longLine.Concat(new byte[] { 10 }).Concat(longLine).Concat(new byte[] { 13, 10 })
                .Concat(longLine).Concat(new byte[] { 121, 10 }).Concat(longLine).ToArray());
            var random = new Random(326152);
            using (var input = new MemoryStream())
            {
                var earlier = new List<byte[]>();
                for (int index = 0; index < 240; index++)
                {
                    byte[] line;
                    if (index % 3 == 0 && earlier.Count != 0) line = earlier[random.Next(earlier.Count)];
                    else
                    {
                        line = new byte[random.Next(0, 150)];
                        random.NextBytes(line);
                        for (int offset = 0; offset < line.Length; offset++)
                            if (line[offset] is 10 or 13 or 26) line[offset] = 0;
                        earlier.Add(line);
                    }
                    input.Write(line);
                    if (index % 2 == 0) input.WriteByte(13);
                    input.WriteByte(10);
                }
                input.Write("final unterminated"u8);
                cases.Add(input.ToArray());
            }
            // These controls exercise the oracle independently of the executable.
            Assert.Equal(Encoding.ASCII.GetBytes("pear" + Environment.NewLine + "apple" + Environment.NewLine),
                Expected("pear\napple\npear\napple"u8.ToArray()));
            Assert.Equal(OperatingSystem.IsWindows()
                ? new byte[] { 97,13,10,97,0,13,10 }
                : new byte[] { 97,13,10,97,0,10,97,10 }, Expected([97,13,13,10,97,0,10,97,13,10]));
            foreach (byte[] input in cases)
            {
                var result = await Run(executable, input);
                Assert.True(result.Code == 0, $"distinct-lines exit {result.Code}: {result.Error}");
                Assert.Empty(result.Error);
                Assert.Equal(Expected(input), result.Output);
            }
            foreach (string[] arguments in new string[][] { ["--unique"], ["extra"], ["one", "two"] })
            {
                var result = await Run(executable, [], arguments);
                Assert.Equal(2, result.Code);
                Assert.Empty(result.Output);
                Assert.Equal("Usage: distinct-lines", result.Error.TrimEnd('\r', '\n'));
            }
        }
        catch (Exception error) { error.Data["DistinctLinesExampleDirectory"] = directory; throw; }
        await TestDirectoryCleanup.DeleteAsync(directory);
    }

    private static byte[] Expected(byte[] physicalInput)
    {
        byte[] input = physicalInput;
        if (OperatingSystem.IsWindows())
        {
            int eof = Array.IndexOf(input, (byte)26);
            if (eof >= 0) input = input[..eof];
            using var translated = new MemoryStream();
            for (int index = 0; index < input.Length; index++)
                if (input[index] != 13 || index + 1 == input.Length || input[index + 1] != 10)
                    translated.WriteByte(input[index]);
            input = translated.ToArray();
        }
        using var output = new MemoryStream();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        byte[] newline = Encoding.ASCII.GetBytes(Environment.NewLine);
        void Keep(ReadOnlySpan<byte> line)
        {
            if (!seen.Add(Convert.ToHexString(line))) return;
            output.Write(line);
            output.Write(newline);
        }
        int start = 0;
        for (int index = 0; index < input.Length; index++)
        {
            if (input[index] != 10) continue;
            int end = index;
            if (end > start && input[end - 1] == 13) end--;
            Keep(input.AsSpan(start, end - start));
            start = index + 1;
        }
        if (start < input.Length) Keep(input.AsSpan(start));
        return output.ToArray();
    }

    private static async Task<(int Code, byte[] Output, string Error)> Run(string executable, byte[] input, params string[] arguments)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        } };
        foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        Assert.True(process.Start());
        using var output = new MemoryStream();
        Task outputTask = process.StandardOutput.BaseStream.CopyToAsync(output);
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            if (input.Length != 0) await process.StandardInput.BaseStream.WriteAsync(input, timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            throw new TimeoutException("distinct-lines timed out.");
        }
        await outputTask;
        return (process.ExitCode, output.ToArray(), await error);
    }
}
