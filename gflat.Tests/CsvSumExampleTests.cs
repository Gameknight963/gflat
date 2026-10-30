using System.Text;

namespace gflat.Tests;

public sealed class CsvSumExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ActualToolValidatesCountedFieldsBeforePrintingAnExactSum(int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-csv-sum-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
                .Select(path => new SourceFile(path, File.ReadAllText(path)))
                .Append(new SourceFile("csv-sum.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples/csv-sum.gf"))));
            string ir = Path.Combine(directory, "main.ll"), executable = Path.Combine(directory, "csv-sum.exe");
            File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            string input = Path.Combine(directory, "input.csv");
            (byte[] Input, string Column, string Sum)[] valid =
            [
                ([], "0", "0"),
                (Bytes("\"bolts, large\",12\r\n\"m4 \"\"nut\"\"\",\"+8\"\r\n"), "1", "20"),
                (Bytes("\"two\r\nlines\",5\nplain,-2"), "1", "3"),
                (Bytes("9223372036854775807\n-9223372036854775808"), "0", "-1"),
                (Bytes("9223372036854775807\n9223372036854775807"), "0", "18446744073709551614"),
                ([255, 0, 44, 52, 10], "1", "4"),
                (Bytes("name,2,extra\nother,3"), "1", "5"),
                (Bytes(new string('0', 63) + "1"), "0", "1")
            ];
            foreach (var sample in valid)
            {
                File.WriteAllBytes(input, sample.Input);
                var result = NativeToolchain.Run(executable, [input, sample.Column], 30000);
                Assert.Equal(0, result.ExitCode);
                Assert.Equal(sample.Sum + Environment.NewLine, result.StandardOutput);
                Assert.Empty(result.StandardError);
            }
            (string Input, string Column, string Diagnostic)[] invalid =
            [
                ("name,\n", "1", "Expected a signed 64-bit integer"),
                ("name\n", "1", "Selected column is missing"),
                ("name,1x", "1", "Expected a signed 64-bit integer"),
                (new string('0', 64) + "1", "0", "64-byte decoded limit"),
                ("\"unterminated", "1", "Invalid CSV at byte 13:"),
                ("ok,2\n\"bad", "1", "Invalid CSV at byte 9:"),
                ("ok,1\rbad,2", "1", "Invalid CSV at byte 5:")
            ];
            foreach (var sample in invalid)
            {
                File.WriteAllText(input, sample.Input);
                var result = NativeToolchain.Run(executable, [input, sample.Column], 30000);
                Assert.Equal(2, result.ExitCode);
                Assert.Empty(result.StandardOutput);
                Assert.Contains(sample.Diagnostic, result.StandardError);
            }
            foreach (string[] arguments in new[] { Array.Empty<string>(), [input], [input, "-1"], [input, "1x"], [input, "0", "extra"] })
            {
                var result = NativeToolchain.Run(executable, arguments, 30000);
                Assert.Equal(2, result.ExitCode);
                Assert.Empty(result.StandardOutput);
                Assert.Equal("Usage: csv-sum <file> <zero-based-column>" + Environment.NewLine, result.StandardError);
            }
            var missing = NativeToolchain.Run(executable, [Path.Combine(directory, "missing.csv"), "0"], 30000);
            Assert.Equal(1, missing.ExitCode);
            Assert.Empty(missing.StandardOutput);
            Assert.NotEmpty(missing.StandardError);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ActualToolReportsImmediateAndBufferedOutputFailure(int optimization)
        => ExampleOutputFailure.AssertReportedWithInput("csv-sum", optimization, "Unable to write CSV sum",
            "name,12\nother,8\n", "1");

    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
}
