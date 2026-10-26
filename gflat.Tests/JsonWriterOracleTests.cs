using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace gflat.Tests;

public sealed class JsonWriterOracleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task WriterRoundTripsTokensWithoutLosingNumbersOrStringContents(int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("json-writer-oracle.gf", """
                using std; using std::Text; using std::Json; using std::IO;
                int main(int argc, char** argv) {
                    String result=new(); JsonWriter writer=new(&result);
                    if(argc==1) {
                        sbyte a=-128; byte b=255; short c=-32768; ushort d=65535;
                        int e=-2147483647-1; uint f=4294967295u;
                        long g=-9223372036854775807l-1l; ulong h=18446744073709551615ul;
                        nint i=-42; nuint j=(nuint)0-(nuint)1;
                        writer.WriteStartArray();
                        writer.WriteNumber(a); writer.WriteNumber(b); writer.WriteNumber(c); writer.WriteNumber(d);
                        writer.WriteNumber(e); writer.WriteNumber(f); writer.WriteNumber(g); writer.WriteNumber(h);
                        writer.WriteNumber(i); writer.WriteNumber(j); writer.WriteRawNumber("-0.000e+999",11);
                        writer.WriteEndArray(); writer.Complete(); Console::WriteLine(ref result); return 0;
                    }
                    String input=FileText::ReadAll(argv[1]); JsonReader reader=new(input.Data,input.Length);
                    char[16384] decoded;
                    while(reader.Read()) {
                        JsonTokenKind kind=reader.Kind;
                        if(kind==JsonTokenKind::StartObject) writer.WriteStartObject();
                        else if(kind==JsonTokenKind::EndObject) writer.WriteEndObject();
                        else if(kind==JsonTokenKind::StartArray) writer.WriteStartArray();
                        else if(kind==JsonTokenKind::EndArray) writer.WriteEndArray();
                        else if(kind==JsonTokenKind::PropertyName || kind==JsonTokenKind::String) {
                            nuint count=0;
                            if(!JsonString::TryUnquote(input.Data+reader.TokenOffset,reader.TokenLength,decoded,16384,ref count)) return 3;
                            if(kind==JsonTokenKind::PropertyName) writer.WritePropertyName(decoded,count);
                            else writer.WriteString(decoded,count);
                        }
                        else if(kind==JsonTokenKind::Number) writer.WriteRawNumber(input.Data+reader.TokenOffset,reader.TokenLength);
                        else if(kind==JsonTokenKind::Null) writer.WriteNull();
                        else writer.WriteBoolean(kind==JsonTokenKind::True);
                    }
                    writer.Complete(); Console::WriteLine(ref result); return 0;
                }
                """));
        string directory = Path.Combine(Path.GetTempPath(), "gflat-json-writer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string ir = Path.Combine(directory, "writer.ll"), executable = Path.Combine(directory, "writer.exe"), input = Path.Combine(directory, "input.json");
            File.WriteAllText(ir, Compiler.Emit(sources, options: new() { InternalizeFunctions = true }));
            var compiled = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var integers = await Run(executable, null);
            Assert.Equal(0, integers.ExitCode); Assert.Empty(integers.Error);
            string pointerMaximum = UIntPtr.Size == 8 ? ulong.MaxValue.ToString() : uint.MaxValue.ToString();
            Assert.Equal("[-128,255,-32768,65535,-2147483648,4294967295,-9223372036854775808,18446744073709551615,-42," +
                pointerMaximum + ",-0.000e+999]" + Environment.NewLine, integers.Output);

            var documents = new List<string>
            {
                "null", "true", "false", "{}", "[]", "\"\"", "[[],{},null,true,false]",
                " \r\n {\"duplicate\":1,\"duplicate\":2} \t\n",
                "[-0,1e9999999999999,-1.2500E+999,123456789012345678901234567890]",
                "{\"\\\"\\\\\\t\\u20ac\":[\"\\uD83D\\uDE00\",\"\\/\\b\\f\\n\\r\\t\\u0000\"]}",
                "[" + new string('[', 512) + "0" + new string(']', 512) + "]",
                JsonSerializer.Serialize(new string('x', 10000) + "\0\"\\\u20ac")
            };
            var options = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true };
            var random = new Random(50713);
            for (int index = 0; index < 24; index++)
            {
                object[] items = Enumerable.Range(0, random.Next(1, 12)).Select(number => (object)new
                {
                    name = $"key {number} \"\\\0\u20ac\ud83d\ude00", number = random.NextInt64(),
                    fraction = (random.NextDouble() - 0.5) * 1e60,
                    nested = new object?[] { null, random.Next(2) == 0, new[] { "", "\t\n\r", "\u2028\u2029" } }
                }).ToArray();
                documents.Add(JsonSerializer.Serialize(items, options));
            }
            foreach (string document in documents)
            {
                byte[] original = Encoding.UTF8.GetBytes(document);
                File.WriteAllBytes(input, original);
                var result = await Run(executable, input);
                Assert.True(result.ExitCode == 0, $"Writer rejected {document}\n{result.Error}");
                Assert.Empty(result.Error);
                Assert.Equal(Tokens(original), Tokens(Encoding.UTF8.GetBytes(result.Output)));
                Assert.Equal(original, File.ReadAllBytes(input));
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string[] Tokens(byte[] bytes)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 2048 });
        var tokens = new List<string>();
        while (reader.Read())
        {
            string payload = reader.TokenType switch
            {
                JsonTokenType.String or JsonTokenType.PropertyName => Convert.ToBase64String(Encoding.UTF8.GetBytes(reader.GetString()!)),
                JsonTokenType.Number => Encoding.UTF8.GetString(reader.ValueSpan),
                _ => ""
            };
            tokens.Add($"{reader.TokenType}:{reader.CurrentDepth}:{payload}");
        }
        return tokens.ToArray();
    }

    private static async Task<(int ExitCode, string Output, string Error)> Run(string executable, string? input)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        if (input != null) start.ArgumentList.Add(input);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); Assert.Fail("JSON writer timed out."); }
        return (process.ExitCode, await output, await error);
    }
}
