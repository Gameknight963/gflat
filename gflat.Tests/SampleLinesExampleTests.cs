using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace gflat.Tests;

public sealed class SampleLinesExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualToolMatchesDeterministicReservoirAndShuffleOracles(int optimization)
    {
        Assert.Equal(0xe220a8397b1dcdafUL, new ReferenceRandom(0).NextWord());
        string directory = Path.Combine(Path.GetTempPath(), "gflat-sample-lines-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
                .Select(path => new SourceFile(path, File.ReadAllText(path)))
                .Append(new SourceFile("sample-lines.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples/sample-lines.gf"))));
            string ir = Path.Combine(directory, "main.ll"), executable = Path.Combine(directory, "sample-lines.exe");
            File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);

            string input = Path.Combine(directory, "input lines.bin");
            ulong maximumCount = (ulong)nuint.MaxValue;
            (string? Argument, ulong Value)[] seeds = [(null, 0), ("42", 42), ("18446744073709551615", ulong.MaxValue)];
            List<byte[]> documents = [[], [10], [10, 10], [13, 10], [13, 13, 10], [13], "one"u8.ToArray(),
                "one\n"u8.ToArray(), "first\r\nsecond\n\nlast\r"u8.ToArray(),
                [0, 255, 128, 26, 13, 10, 195, 40, 10, 0, 13]];
            documents.Add(Enumerable.Repeat((byte)'x', 18000).Concat(new byte[] { 13, 10, 255, 0, 26, 13 }).ToArray());
            using (var many = new MemoryStream())
            {
                for (int line = 0; line < 96; line++)
                {
                    many.Write(Encoding.ASCII.GetBytes($"line-{line:D3}"));
                    if (line % 11 == 0) many.Write([0, 26, 255]);
                    if (line == 95) break;
                    if (line % 2 == 0) many.WriteByte(13);
                    many.WriteByte(10);
                }
                documents.Add(many.ToArray());
            }
            var random = new Random(418937);
            using (var arbitrary = new MemoryStream())
            {
                for (int line = 0; line < 48; line++)
                {
                    byte[] bytes = new byte[random.Next(65)];
                    random.NextBytes(bytes);
                    for (int index = 0; index < bytes.Length; index++)
                        if (bytes[index] == 10) bytes[index] = 255;
                    arbitrary.Write(bytes);
                    if (line % 3 == 0) arbitrary.WriteByte(13);
                    arbitrary.WriteByte(10);
                }
                arbitrary.Write([255, 0, 26, 13]);
                documents.Add(arbitrary.ToArray());
            }

            for (int profile = 0; profile < documents.Count; profile++)
            {
                byte[] document = documents[profile];
                File.WriteAllBytes(input, document);
                List<ulong> counts = [1, 3];
                if (profile % 3 == 0) counts.Add(maximumCount);
                else if (profile % 3 == 1) counts.Add(1000);
                for (int row = 0; row < counts.Count; row++)
                {
                    ulong count = counts[row];
                    var seed = seeds[(profile + row) % seeds.Length];
                    List<string> arguments = [input, count.ToString(CultureInfo.InvariantCulture)];
                    if (seed.Argument != null) arguments.Add(seed.Argument);
                    var result = await Run(executable, arguments);
                    Assert.True(result.Code == 0, $"Sample exit {result.Code}: {Encoding.UTF8.GetString(result.Error)}");
                    Assert.Empty(result.Error);
                    Assert.Equal(Expected(document, count, seed.Value), result.Output);
                    Assert.Equal(document, File.ReadAllBytes(input));
                }
            }

            byte[] identified = Encoding.ASCII.GetBytes("zero\none\ntwo\nthree\nfour\nfive\nsix");
            File.WriteAllBytes(input, identified);
            foreach (var seed in seeds)
            {
                string seedText = seed.Value.ToString(CultureInfo.InvariantCulture);
                var exact = await Run(executable, [input, "7", seedText]);
                var larger = await Run(executable, [input, "8", seedText]);
                var repeated = await Run(executable, [input, "7", seedText]);
                Assert.Equal(0, exact.Code);
                Assert.Equal(0, larger.Code);
                Assert.Equal(0, repeated.Code);
                Assert.Empty(exact.Error);
                Assert.Empty(larger.Error);
                Assert.Empty(repeated.Error);
                Assert.Equal(Expected(identified, 7, seed.Value), exact.Output);
                Assert.Equal(exact.Output, larger.Output);
                Assert.Equal(exact.Output, repeated.Output);
            }
            var decorated = await Run(executable, [input, "+0003", "+00042"]);
            Assert.Equal(0, decorated.Code);
            Assert.Empty(decorated.Error);
            Assert.Equal(Expected(identified, 3, 42), decorated.Output);
            await ExpectError(executable, [input, "0", "18446744073709551615"], 0, "");

            string missing = Path.Combine(directory, "missing.bin");
            foreach (string count in new[] { "0", "+000" })
                await ExpectError(executable, [missing, count], 0, "");
            await ExpectError(executable, [missing, "1"], 1, "sample-lines: File open failed");
            var unreadable = await Run(executable, [directory, "1"]);
            Assert.Equal(1, unreadable.Code);
            Assert.Empty(unreadable.Output);
            Assert.StartsWith("sample-lines: ", Encoding.UTF8.GetString(unreadable.Error));
            foreach (string count in new[] { "", "-1", "-0", "+", " 1", "1 ", "1.0", "0x1", "1_0",
                ((BigInteger)maximumCount + 1).ToString(CultureInfo.InvariantCulture) })
                await ExpectError(executable, [missing, count], 2, "sample-lines: count must be a nonnegative decimal integer");
            foreach (string seed in new[] { "", "-1", "-0", "+", " 1", "1 ", "1.0", "0x1", "1_0", "18446744073709551616" })
            {
                foreach (string count in new[] { "0", "1" })
                    await ExpectError(executable, [missing, count, seed], 2, "sample-lines: seed must be an unsigned 64-bit decimal integer");
            }
            foreach (string[] arguments in new string[][] { [], [missing], [missing, "1", "2", "extra"] })
                await ExpectError(executable, arguments, 2, "Usage: sample-lines <input-file> <count> [seed]");
            Assert.Equal(identified, File.ReadAllBytes(input));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualToolReportsBufferedAndImmediateOutputFailures(int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-sample-lines-output-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.txt");
            File.WriteAllText(input, "first\nsecond\n", new UTF8Encoding(false));
            ExampleOutputFailure.AssertReportedWithArguments("sample-lines", optimization,
                "sample-lines: Console flush failed", "sample-lines: Console write failed", input, "1");
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory); }
    }

    private static byte[] Expected(byte[] input, ulong count, ulong seed)
    {
        if (count == 0) return [];
        var lines = new List<byte[]>();
        int beginning = 0;
        for (int index = 0; index < input.Length; index++)
        {
            if (input[index] != 10) continue;
            int end = index;
            if (end > beginning && input[end - 1] == 13) end--;
            lines.Add(input[beginning..end]);
            beginning = index + 1;
        }
        if (beginning < input.Length) lines.Add(input[beginning..]);

        var random = new ReferenceRandom(seed);
        var sample = new List<byte[]>();
        ulong seen = 0;
        foreach (byte[] line in lines)
        {
            seen++;
            if ((ulong)sample.Count < count) sample.Add(line);
            else
            {
                ulong selected = random.Below(seen);
                if (selected < count) sample[(int)selected] = line;
            }
        }
        for (int remaining = sample.Count; remaining > 1; remaining--)
        {
            int selected = (int)random.Below((ulong)remaining);
            (sample[remaining - 1], sample[selected]) = (sample[selected], sample[remaining - 1]);
        }
        using var output = new MemoryStream();
        byte[] newline = Encoding.ASCII.GetBytes(Environment.NewLine);
        foreach (byte[] line in sample)
        {
            output.Write(line);
            output.Write(newline);
        }
        return output.ToArray();
    }

    private sealed class ReferenceRandom(ulong seed)
    {
        private ulong state = seed;

        public ulong NextWord()
        {
            state = unchecked(state + 0x9e3779b97f4a7c15UL);
            ulong value = state;
            value = unchecked((value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL);
            value = unchecked((value ^ (value >> 27)) * 0x94d049bb133111ebUL);
            return value ^ (value >> 31);
        }

        public ulong Below(ulong bound)
        {
            BigInteger threshold = (BigInteger.One << 64) % bound;
            ulong word;
            do { word = NextWord(); } while ((BigInteger)word < threshold);
            return (ulong)((BigInteger)word % bound);
        }
    }

    private static async Task ExpectError(string executable, string[] arguments, int code, string diagnostic)
    {
        var result = await Run(executable, arguments);
        Assert.Equal(code, result.Code);
        Assert.Empty(result.Output);
        byte[] expected = diagnostic.Length == 0 ? [] : Encoding.UTF8.GetBytes(diagnostic + Environment.NewLine);
        Assert.Equal(expected, result.Error);
    }

    private static async Task<(int Code, byte[] Output, byte[] Error)> Run(string executable, IEnumerable<string> arguments)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(executable) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        } };
        foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        Assert.True(process.Start());
        process.StandardInput.Close();
        using var output = new MemoryStream();
        using var error = new MemoryStream();
        Task stdout = process.StandardOutput.BaseStream.CopyToAsync(output);
        Task stderr = process.StandardError.BaseStream.CopyToAsync(error);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
            throw new TimeoutException("sample-lines timed out.");
        }
        await Task.WhenAll(stdout, stderr);
        return (process.ExitCode, output.ToArray(), error.ToArray());
    }
}
