using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace gflat.Tests;

public sealed class JsonReaderOracleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ReaderMatchesIndependentTokenOffsetsAndDepthsAndRejectsInvalidDocuments(int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("json-reader-oracle.gf", """
                using std; using std::Text; using std::Json; using std::IO;
                int main(int argc, char** argv) {
                    if (argc != 2) return 2;
                    String text=FileText::ReadAll(argv[1]);
                    JsonReader reader=new(text.Data,text.Length);
                    try {
                        while(reader.Read()) {
                            String row=s$"{(int)reader.Kind},{reader.TokenOffset},{reader.TokenLength},{reader.Depth}";
                            Console::WriteLine(ref row);
                        }
                        return 0;
                    } catch(JsonParseException* error) { return 1; }
                }
                """));
        string directory = Path.Combine(Path.GetTempPath(), "gflat-json-oracle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string ir = Path.Combine(directory, "reader.ll"), executable = Path.Combine(directory, "reader.exe"), input = Path.Combine(directory, "document.json");
            File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var documents = new List<string>
            {
                "null", "true", "false", "0", "-0", "123456789012345678901234567890", "-1.25e+12345",
                "\"\"", "\"raw \u20ac\ud83d\ude00\"", "[]", "{}", "[[],{},null,true,false]",
                " \r\n\t{\"duplicate\":1,\"duplicate\":2}\t\r\n ",
                "{\"\\\"\\\\\\t\\u20ac\":[\"\\uD83D\\uDE00\",\"\\/\\b\\f\\n\\r\\t\"]}",
                "[" + new string('[', 512) + "0" + new string(']', 512) + "]",
                JsonSerializer.Serialize(new string('x', 10000) + "\0\"\\\u20ac")
            };
            var random = new Random(84217);
            for (int index = 0; index < 32; index++)
                documents.Add(JsonSerializer.Serialize(Value(random, 0), new JsonSerializerOptions { WriteIndented = index % 2 == 0 }));
            foreach (string document in documents)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(document);
                File.WriteAllBytes(input, bytes);
                var result = await Run(executable, input);
                Assert.True(result.ExitCode == 0, $"Rejected valid document: {document}\n{result.Error}");
                Assert.Empty(result.Error);
                Assert.Equal(Expected(bytes), result.Output.Replace("\r\n", "\n"));
            }
            string[] invalid = ["", " \t\r\n", "[", "{", "[1", "{\"x\"", "{\"x\":", "[1,]", "{\"x\":1,}",
                "[,1]", "{,}", "{x:1}", "{\"x\" 1}", "{\"x\":1 \"y\":2}", "[1 2]", "true false", "{}[]",
                "tru", "TRUE", "False", "nul", "undefined", "+1", "01", "-01", "1.", ".1", "1e", "1e+", "--1",
                "NaN", "Infinity", "/*comment*/0", "[1,//comment\n2]", "\ufeff{}", "\"unterminated", "\"\\x\"", "\"raw\tcontrol\"", "[}\n", "{]", "0\0"];
            foreach (string document in invalid)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(document);
                Assert.ThrowsAny<JsonException>(() => { using var parsed = JsonDocument.Parse(bytes); });
                await Rejected(bytes);
            }
            // These extra strict cases match JsonString's Unicode policy. Some
            // parsers postpone Unicode validation until a string is decoded, so
            // do not use their token-only acceptance as the oracle here.
            foreach (byte[] bytes in new byte[][]
            {
                Encoding.UTF8.GetBytes("\"\\uD800\""), Encoding.UTF8.GetBytes("{\"\\uDC00\":1}"),
                [34, 0xff, 34], [34, 0xc0, 0x80, 34], [34, 0xed, 0xa0, 0x80, 34], [34, 0xf4, 0x90, 0x80, 0x80, 34]
            }) await Rejected(bytes);

            async Task Rejected(byte[] bytes)
            {
                File.WriteAllBytes(input, bytes);
                var result = await Run(executable, input);
                Assert.True(result.ExitCode == 1, $"Expected JSON error for {Convert.ToHexString(bytes)}, got {result.ExitCode}: {result.Error}");
                Assert.Empty(result.Error);
            }
        }
        catch (Exception error)
        {
            // Keep the fixture and original failure; cleanup must not replace it.
            error.Data["JsonReaderOracleDirectory"] = directory;
            throw;
        }
        await TestDirectoryCleanup.DeleteAsync(directory);
    }

    private static object? Value(Random random, int depth)
    {
        switch (random.Next(depth >= 4 ? 5 : 7))
        {
            case 0: return null;
            case 1: return random.Next(2) == 0;
            case 2: return random.NextInt64(-1000000000000L, 1000000000000L);
            case 3: return (random.NextDouble() - 0.5) * 1e50;
            case 4: return new[] { "", "plain", "\"\\\0\t\n", "\u20ac\ud83d\ude00", new string('z', 70) }[random.Next(5)];
            case 5: return Enumerable.Range(0, random.Next(6)).Select(_ => Value(random, depth + 1)).ToArray();
            default: return Enumerable.Range(0, random.Next(6)).ToDictionary(index => $"key {index} \u20ac", _ => Value(random, depth + 1));
        }
    }

    private static string Expected(byte[] bytes)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 2048 });
        var output = new StringBuilder();
        while (reader.Read())
        {
            // JsonTokenKind deliberately omits .NET's optional Comment token.
            int kind = reader.TokenType switch
            {
                JsonTokenType.StartObject => 1, JsonTokenType.EndObject => 2,
                JsonTokenType.StartArray => 3, JsonTokenType.EndArray => 4,
                JsonTokenType.PropertyName => 5, JsonTokenType.String => 6,
                JsonTokenType.Number => 7, JsonTokenType.True => 8,
                JsonTokenType.False => 9, JsonTokenType.Null => 10,
                _ => throw new InvalidOperationException()
            };
            int length = reader.TokenType switch
            {
                JsonTokenType.PropertyName or JsonTokenType.String => reader.ValueSpan.Length + 2,
                JsonTokenType.Number => reader.ValueSpan.Length,
                JsonTokenType.True or JsonTokenType.Null => 4,
                JsonTokenType.False => 5,
                _ => 1
            };
            output.Append($"{kind},{reader.TokenStartIndex},{length},{reader.CurrentDepth}\n");
        }
        return output.ToString();
    }

    private static async Task<(int ExitCode, string Output, string Error)> Run(string executable, string input)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(input);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); Assert.Fail("JSON reader timed out."); }
        return (process.ExitCode, await output, await error);
    }
}
