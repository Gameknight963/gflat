using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace gflat.Tests;

public sealed class CsvToJsonExampleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ActualToolPreservesTextRowsAndValidatesEntireInputBeforeOutput(int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-csv-json-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
                .Select(path => new SourceFile(path, File.ReadAllText(path)))
                .Append(new SourceFile("csv-to-json.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples/csv-to-json.gf"))));
            string ir = Path.Combine(directory, "main.ll"), executable = Path.Combine(directory, "csv-to-json.exe");
            File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            string input = Path.Combine(directory, "input.csv");
            var valid = new List<(string Input, string[][] Rows)>
            {
                ("", []),
                ("\r\n", [[""]]),
                ("\n\n", [[""], [""]]),
                (",\nlast,", [["", ""], ["last", ""]]),
                ("\"bolts, large\",12\r\n\"m4 \"\"nut\"\"\",8", [["bolts, large", "12"], ["m4 \"nut\"", "8"]]),
                ("\"two\r\nlines\",\"bare\rCR\",\"bare\nLF\"", [["two\r\nlines", "bare\rCR", "bare\nLF"]]),
                ("first\0field,\"second\0field\"\n", [["first\0field", "second\0field"]]),
                ("\uFEFFname,01\nother,+2,extra\n", [["\uFEFFname", "01"], ["other", "+2", "extra"]]),
                (" leading , trailing \n", [[" leading ", " trailing "]])
            };
            var random = new Random(37539);
            string[] fragments = ["x", ",", "\"", "\0", "\r", "\n", "\t", " ", "\u00e9", "\u65e5\u672c\u8a9e", "\U0001f600"];
            for (int document = 0; document < 50; document++)
            {
                string[][] rows = Enumerable.Range(0, random.Next(1, 7)).Select(_ =>
                    Enumerable.Range(0, random.Next(1, 7)).Select(_ =>
                        string.Concat(Enumerable.Range(0, random.Next(0, 25)).Select(_ => fragments[random.Next(fragments.Length)]))).ToArray()).ToArray();
                valid.Add((EncodeCsv(rows), rows));
            }
            string[][] large = [[new string('a', 20000) + "\"\0\U0001f600", "tail"], ["small"]];
            valid.Add((EncodeCsv(large), large));
            foreach (var sample in valid)
            {
                File.WriteAllBytes(input, Encoding.UTF8.GetBytes(sample.Input));
                var result = await Run(executable, [input]);
                Assert.True(result.Code == 0, result.Error);
                Assert.Empty(result.Error);
                using JsonDocument document = JsonDocument.Parse(result.Output);
                Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
                JsonElement[] rows = document.RootElement.EnumerateArray().ToArray();
                Assert.Equal(sample.Rows.Length, rows.Length);
                for (int row = 0; row < rows.Length; row++)
                {
                    Assert.Equal(JsonValueKind.Array, rows[row].ValueKind);
                    JsonElement[] fields = rows[row].EnumerateArray().ToArray();
                    Assert.Equal(sample.Rows[row].Length, fields.Length);
                    for (int column = 0; column < fields.Length; column++)
                    {
                        Assert.Equal(JsonValueKind.String, fields[column].ValueKind);
                        Assert.Equal(sample.Rows[row][column], fields[column].GetString());
                    }
                }
            }

            (byte[] Input, string Diagnostic)[] invalid =
            [
                (Encoding.UTF8.GetBytes("ok,1\n\"unterminated"), "Invalid CSV at byte"),
                (Encoding.UTF8.GetBytes("ok\n\"closed\"suffix"), "Invalid CSV at byte"),
                (Encoding.UTF8.GetBytes("ok\nbare\r"), "Invalid CSV at byte"),
                (Encoding.UTF8.GetBytes("ok\nbad\"quote"), "Invalid CSV at byte"),
                ([111, 107, 10, 255], "CSV field is not valid UTF-8"),
                ([111, 107, 10, 0xc0, 0x80], "CSV field is not valid UTF-8"),
                ([111, 107, 10, 0xed, 0xa0, 0x80], "CSV field is not valid UTF-8"),
                ([111, 107, 10, 0xf4, 0x90, 0x80, 0x80], "CSV field is not valid UTF-8"),
                ([111, 107, 10, 0xe2, 0x82], "CSV field is not valid UTF-8"),
                ([111, 107, 10, 34, 0x80, 34], "CSV field is not valid UTF-8")
            ];
            foreach (var sample in invalid)
            {
                File.WriteAllBytes(input, sample.Input);
                var result = await Run(executable, [input]);
                Assert.Equal(2, result.Code);
                Assert.Empty(result.Output);
                Assert.Contains(sample.Diagnostic, result.Error);
            }
            foreach (string[] arguments in new[] { Array.Empty<string>(), new[] { input, "extra" } })
            {
                var result = await Run(executable, arguments);
                Assert.Equal(2, result.Code);
                Assert.Empty(result.Output);
                Assert.Equal("Usage: csv-to-json <file>" + Environment.NewLine, result.Error);
            }
            var missing = await Run(executable, [Path.Combine(directory, "missing.csv")]);
            Assert.Equal(1, missing.Code);
            Assert.Empty(missing.Output);
            Assert.NotEmpty(missing.Error);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ActualToolReportsImmediateAndBufferedOutputFailure(int optimization)
        => ExampleOutputFailure.AssertReportedWithInput("csv-to-json", optimization, "Unable to write CSV JSON",
            "name,12\nother,8\n");

    private static string EncodeCsv(string[][] rows)
        => string.Concat(rows.Select(row => string.Join(",", row.Select(field =>
            field.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + field.Replace("\"", "\"\"") + "\"" : field)) + "\r\n"));

    private static async Task<(int Code, string Output, string Error)> Run(string executable, string[] arguments)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        } };
        foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        Assert.True(process.Start());
        Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            throw new TimeoutException("csv-to-json timed out.");
        }
        return (process.ExitCode, await output, await error);
    }
}
