using System.Diagnostics;
using System.Text;

namespace gflat.Tests;

public sealed class SortLinesExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualToolSortsCountedLinesByUnsignedBytes(int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("sort-lines.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples/sort-lines.gf"))));
        string directory = Path.Combine(Path.GetTempPath(), "gflat_sort_lines_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string ir = Path.Combine(directory, "main.ll"), executable = Path.Combine(directory, "sort-lines.exe");
            File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            byte[] longPrefix = Enumerable.Repeat((byte)'x', 16000).ToArray();
            List<byte[]> cases = [[], [10], [10, 10, 10], "one"u8.ToArray(), "one\n"u8.ToArray(),
                " beta\r\nAlpha\nbeta\r\n\nalpha"u8.ToArray(),
                [97, 0, 98, 10, 97, 0, 10, 97, 10, 255, 10, 128, 10, 0],
                "same\nsame\r\nsame\n"u8.ToArray(),
                "b\rc\na\r\r\nbare\r"u8.ToArray(),
                longPrefix.Concat(new byte[] { 98, 10 }).Concat(longPrefix).Concat(new byte[] { 97, 13, 10 }).Concat(longPrefix).ToArray()];
            byte[] carriageReturnCorner = [97, 13, 13, 10, 97, 0, 10];
            byte[] cornerOutput = OperatingSystem.IsWindows()
                ? [97, 13, 10, 97, 0, 13, 10] // Logical "a" sorts before "a\0".
                : [97, 0, 10, 97, 13, 10]; // Logical "a\0" sorts before "a\r".
            Assert.Equal(cornerOutput, Expected(carriageReturnCorner));
            cases.Add(carriageReturnCorner);
            byte[] eofInsideLine = [122,10,97,98,26,105,103,110,111,114,101,100,10,97,10];
            byte[] eofExpected = OperatingSystem.IsWindows()
                ? [97,98,13,10,122,13,10] // Ctrl+Z ends input after the partial "ab" record.
                : [97,10,97,98,26,105,103,110,111,114,101,100,10,122,10];
            Assert.Equal(eofExpected, Expected(eofInsideLine));
            cases.Add(eofInsideLine);
            cases.Add([26,110,101,118,101,114,10]); // EOF before any Windows input; ordinary data elsewhere.
            cases.Add([122,13,10,97,98,13,26,105,103,110,111,114,101,100,10]);
            var random = new Random(105032);
            using (var input = new MemoryStream())
            {
                for (int line = 0; line < 160; line++)
                {
                    byte[] bytes = new byte[random.Next(0, 97)];random.NextBytes(bytes);
                    // Keep random data away from line delimiters and the Windows text-mode EOF byte.
                    for (int i = 0; i < bytes.Length; i++) if (bytes[i] is 10 or 13 or 26) bytes[i] = 0;
                    input.Write(bytes);
                    if (line % 2 == 0) input.WriteByte(13);
                    input.WriteByte(10);
                    if (line % 10 == 0) { input.Write(bytes);input.WriteByte(10); }
                }
                input.Write("final unterminated"u8);
                cases.Add(input.ToArray());
            }
            // Exact counted duplicates include embedded NUL and high bytes, plus distinct C-string prefixes/case.
            cases.Add([97,0,98,10,97,0,98,10,97,10,97,0,10,97,0,10,255,128,0,10,255,128,0,10,255,128,10,65,10,97]);
            foreach (byte[] input in cases)
            foreach (bool unique in new[] { false, true })
            {
                string[] arguments = unique ? ["--unique"] : [];
                var result = await Run(executable, input, arguments);
                Assert.True(result.Code == 0, $"sort-lines unique={unique} exit {result.Code}: {result.Error}");
                Assert.Empty(result.Error);
                Assert.Equal(Expected(input, unique), result.Output);
            }
            foreach (string[] arguments in new string[][] { ["--unknown"], ["--unique", "extra"], ["--unique", "--unique"] })
            {
                var result = await Run(executable, [], arguments);
                Assert.Equal(2, result.Code);
                Assert.Empty(result.Output);
                Assert.Equal("Usage: sort-lines [--unique]", result.Error.TrimEnd('\r', '\n'));
            }
        }
        catch (Exception error) { error.Data["SortLinesExampleDirectory"] = directory; throw; }
        await TestDirectoryCleanup.DeleteAsync(directory);
    }

    private static byte[] Expected(byte[] input, bool unique = false)
    {
        if (OperatingSystem.IsWindows())
        {
            int endOfFile = Array.IndexOf(input, (byte)26);
            if (endOfFile >= 0) input = input[..endOfFile];
            // The existing CRT text stream translates physical CRLF before the
            // library sees bytes. A remaining CR may then precede the logical LF.
            using var translated = new MemoryStream();
            for (int i = 0; i < input.Length; i++)
                if (input[i] != 13 || i + 1 == input.Length || input[i + 1] != 10)
                    translated.WriteByte(input[i]);
            input = translated.ToArray();
        }
        var lines = new List<byte[]>();
        int start = 0;
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] != 10) continue;
            int end = i;
            if (end > start && input[end - 1] == 13) end--;
            lines.Add(input[start..end]);start = i + 1;
        }
        if (start < input.Length) lines.Add(input[start..]);
        lines.Sort((left, right) => left.AsSpan().SequenceCompareTo(right));
        byte[] newline = Encoding.ASCII.GetBytes(Environment.NewLine);
        using var output = new MemoryStream();
        IEnumerable<byte[]> selected = lines;
        if (unique) selected = lines.GroupBy(line => Convert.ToHexString(line), StringComparer.Ordinal).Select(group => group.First());
        foreach (byte[] line in selected) { output.Write(line);output.Write(newline); }
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
            throw new TimeoutException("sort-lines timed out.");
        }
        await outputTask;
        return (process.ExitCode, output.ToArray(), await error);
    }
}
