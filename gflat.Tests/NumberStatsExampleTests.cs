using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace gflat.Tests;

public sealed class NumberStatsExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualStreamingToolProducesExactWideStatisticsAndLineDiagnostics(int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("number-stats.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples/number-stats.gf"))));
        string directory = Path.Combine(Path.GetTempPath(), "gflat_number_stats_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string ir = Path.Combine(directory, "main.ll"), executable = Path.Combine(directory, "number-stats.exe");
            File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);

            var cases = new List<(string Name, byte[] Input, int Code, string Output, string Error)>();
            void Valid(string name, string input, params long[] values)
                => cases.Add((name, Encoding.UTF8.GetBytes(input), 0, Expected(values), ""));
            Valid("empty", "");
            Valid("blank", " \t\r\n\n\v\f\r\n");
            string maximum = long.MaxValue.ToString(CultureInfo.InvariantCulture), minimum = long.MinValue.ToString(CultureInfo.InvariantCulture);
            Valid("above Int64 maximum sum", $"{maximum}\n{maximum}\n", long.MaxValue, long.MaxValue);
            Valid("below Int64 minimum sum", $"{minimum}\n{minimum}\n", long.MinValue, long.MinValue);
            Valid("full range, CRLF, trim and final unterminated", $" \t{minimum} \r\n{maximum}\n+1", long.MinValue, long.MaxValue, 1);
            Valid("127 payload bytes", new string('0', 127) + "\n", 0);
            var random = new Random(2064128);
            long[] numbers = Enumerable.Range(0, 250).Select(_ => random.NextInt64(long.MinValue, long.MaxValue)).Concat([long.MinValue, -1, 0, long.MaxValue]).ToArray();
            Valid("streamed mixed values", string.Join("\r\n", numbers.Select(value => " \t" + value.ToString(CultureInfo.InvariantCulture) + " ")) + "\r\n", numbers);
            cases.Add(("malformed line 3", "12\n\t \n12suffix\n34\n"u8.ToArray(), 2, "", Invalid(3)));
            cases.Add(("numeric overflow line 3", "12\n\n9223372036854775808\n"u8.ToArray(), 2, "", Invalid(3)));
            cases.Add(("embedded zero", [49, 0, 10], 2, "", Invalid(1)));
            cases.Add(("high byte", [49, 255, 10], 2, "", Invalid(1)));
            cases.Add(("Unicode nonbreaking space", [194, 160, 49, 10], 2, "", Invalid(1)));
            cases.Add(("128 payload bytes", Encoding.ASCII.GetBytes(new string('0', 128) + "\n1\n"), 2, "", Oversized(1)));
            cases.Add(("oversized blank line 3", Encoding.ASCII.GetBytes("1\n\n" + new string(' ', 128) + "\n"), 2, "", Oversized(3)));
            foreach (var sample in cases)
            {
                var result = await Run(executable, sample.Input);
                Assert.True(result.Code == sample.Code, $"{sample.Name}: exit {result.Code}, stderr {result.Error}");
                Assert.Equal(sample.Output, Normalize(result.Output));
                Assert.Equal(sample.Error, Normalize(result.Error));
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string Invalid(int line) => $"Line {line}: Expected a signed 64-bit decimal integer\n";
    private static string Oversized(int line) => $"Line {line}: Line exceeds the 127-byte payload limit\n";
    private static string Normalize(string text) => text.Replace("\r\n", "\n");
    private static string Expected(long[] values)
    {
        BigInteger sum = values.Aggregate(BigInteger.Zero, (total, value) => total + value);
        string output = "Count: " + values.Length.ToString(CultureInfo.InvariantCulture) + "\nSum: " + sum.ToString(CultureInfo.InvariantCulture) + "\n";
        if (values.Length != 0)
            output += "Min: " + values.Min().ToString(CultureInfo.InvariantCulture) + "\nMax: " + values.Max().ToString(CultureInfo.InvariantCulture) + "\n";
        return output;
    }

    private static async Task<(int Code, string Output, string Error)> Run(string executable, byte[] input)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
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
            throw new TimeoutException("number-stats timed out.");
        }
        return (process.ExitCode, await output, await error);
    }
}
