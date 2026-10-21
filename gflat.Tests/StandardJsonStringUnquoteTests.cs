using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace gflat.Tests;

public sealed class StandardJsonStringUnquoteTests
{
    private static readonly JsonSerializerOptions RawUnicode = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static void Assign(StringBuilder code, string target, byte[] bytes, int start = 0)
    {
        for (int i = 0; i < bytes.Length; i++) code.AppendLine($"{target}[{start + i}]=(char){bytes[i]};");
    }

    private static void Run(string program, int optimization)
    {
        SourceFile[] sources = new[] { "Utf8.gf", "JsonString.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("main.gf", program)).ToArray();
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true });
        foreach (string forbidden in new[] { "@__gflat_eh_", "personality", "gflat$Memory$", "@malloc", "@free" }) Assert.DoesNotContain(forbidden, ir);
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EscapedAndRawScalarsMatchDotNetIncludingInPlaceDecoding(int optimization)
    {
        var texts = new List<string> { "", "plain / text", "\0\b\t\n\f\r\"\\/", new(Enumerable.Range(0, 128).Select(value => (char)value).ToArray()) };
        int[] boundaries = [0x7f, 0x80, 0x7ff, 0x800, 0xd7ff, 0xe000, 0xfeff, 0xfffe, 0xffff, 0x10000, 0x10ffff, 0x2028, 0x2029];
        texts.AddRange(boundaries.Select(char.ConvertFromUtf32));
        var random = new Random(971526);
        for (int test = 0; test < 12; test++)
        {
            var text = new StringBuilder();
            for (int i = 0; i < 8; i++)
            {
                int scalar = random.Next(0x110000);
                if (scalar is >= 0xd800 and <= 0xdfff) scalar = 0;
                text.Append(char.ConvertFromUtf32(scalar));
            }
            texts.Add(text.ToString());
        }
        var literals = texts.SelectMany(text => new[] { JsonSerializer.SerializeToUtf8Bytes(text), JsonSerializer.SerializeToUtf8Bytes(text, RawUnicode) }).ToList();
        literals.Add(Encoding.UTF8.GetBytes("\"\\/\\\\\\\"\\b\\f\\n\\r\\t\\u0000\\u0041\\u00e9\\uD834\\udd1e\""));
        int maximumInput = literals.Max(bytes => bytes.Length);
        int maximumOutput = literals.Max(bytes => Encoding.UTF8.GetByteCount(JsonDocument.Parse(bytes).RootElement.GetString()!));
        var code = new StringBuilder($"using std; int main() {{ char[{maximumInput + 2}] input; char[{maximumOutput + 2}] output; char[{maximumOutput + 1}] expected; nuint needed=0; nuint written=0;\n");
        foreach (byte[] literal in literals)
        {
            using var parsed = JsonDocument.Parse(literal);
            byte[] expected = Encoding.UTF8.GetBytes(parsed.RootElement.GetString()!);
            Assign(code, "input", literal, 1); Assign(code, "expected", expected);
            code.AppendLine($$"""
                input[0]='!'; input[{{literal.Length + 1}}]='?';
                for(nuint i=0;i<{{maximumOutput + 2}};i++)output[i]='Z';
                needed=999;written=999;
                if(!JsonString::TryGetUnquotedLength(&input[1],{{literal.Length}},ref needed)||needed!={{expected.Length}})return 1;
                if(!JsonString::TryUnquote(&input[1],{{literal.Length}},&output[1],{{expected.Length}},ref written)||written!=needed)return 2;
                for(nuint i=0;i<written;i++)if(output[i+1]!=expected[i])return 3;
                if(output[0]!='Z'||output[written+1]!='Z'||input[0]!='!'||input[{{literal.Length + 1}}]!='?')return 4;
                written=999;
                if(!JsonString::TryUnquote(&input[1],{{literal.Length}},&input[1],{{expected.Length}},ref written)||written!={{expected.Length}})return 5;
                for(nuint i=0;i<written;i++)if(input[i+1]!=expected[i])return 6;
                if(input[0]!='!'||input[{{literal.Length + 1}}]!='?')return 7;
                """);
        }
        code.AppendLine("return 0; }");
        Run(code.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryShortCapacityAndMalformedSuffixLeavesAllBytesUntouched(int optimization)
    {
        byte[] literal = Encoding.UTF8.GetBytes("\"prefix\\n\\u0000\\u00e9\\uD834\\uDD1e/\u20ac\"");
        using var parsed = JsonDocument.Parse(literal);
        int decodedLength = Encoding.UTF8.GetByteCount(parsed.RootElement.GetString()!);
        var code = new StringBuilder("using std; int main() {char[256] input;char[256] original;char[256] output;nuint written=0;nuint needed=0;\n");
        Assign(code, "input", literal, 1); Assign(code, "original", literal, 1);
        code.AppendLine($$"""
            input[0]='!';input[{{literal.Length + 1}}]='?';
            for(nuint capacity=0;capacity<{{decodedLength}};capacity++) {
                for(nuint i=0;i<256;i++)output[i]='Z';
                written=999;
                if(JsonString::TryUnquote(&input[1],{{literal.Length}},&output[1],capacity,ref written)||written!=0)return 1;
                for(nuint i=0;i<256;i++)if(output[i]!='Z')return 2;
                written=999;
                if(JsonString::TryUnquote(&input[1],{{literal.Length}},&input[1],capacity,ref written)||written!=0)return 3;
                for(nuint i=1;i<{{literal.Length + 1}};i++)if(input[i]!=original[i])return 4;
                if(input[0]!='!'||input[{{literal.Length + 1}}]!='?')return 5;
            }
            """);
        var malformed = new List<byte[]>();
        string[] escapes = ["\\", "\\x", "\\U0000", "\\v", "\\0", "\\u", "\\u0", "\\u00", "\\u000", "\\u000G", "\\uGGGG", "\\uD800", "\\uDBFF", "\\uDC00", "\\uDFFF", "\\uD800\\u0041", "\\uD800x", "\\uD800\\uD800", "\\uD800\\uDC0G", "\\uD800/\\uDC00", "\""];
        byte[] prefix = Encoding.UTF8.GetBytes("\"valid prefix valid prefix ");
        foreach (string escape in escapes) malformed.Add(prefix.Concat(Encoding.UTF8.GetBytes(escape)).Append((byte)'"').ToArray());
        for (int control = 0; control < 32; control++) malformed.Add(prefix.Concat(new byte[] { (byte)control, (byte)'"' }).ToArray());
        byte[][] utf8 = [[0x80], [0xc0,0x80], [0xc2], [0xe0,0x9f,0xbf], [0xed,0xa0,0x80], [0xf0,0x8f,0xbf,0xbf], [0xf4,0x90,0x80,0x80], [0xf5,0x80,0x80,0x80], [0xff], [0xe2,0x28,0xa1]];
        foreach (byte[] bytes in utf8) malformed.Add(prefix.Concat(bytes).Append((byte)'"').ToArray());
        malformed.AddRange(new[] { "", "\"", "plain", " \"x\"", "\"x\" ", "\"a\"\"b\"", "\"x\"x", "\"unterminated", "\uFEFF\"x\"" }.Select(Encoding.UTF8.GetBytes));
        foreach (byte[] input in malformed)
        {
            Assign(code, "input", input, 1); Assign(code, "original", input, 1);
            code.AppendLine($$"""
                input[0]='!';input[{{input.Length + 1}}]='?';
                for(nuint i=0;i<256;i++)output[i]='Z';
                needed=999;written=999;
                if(JsonString::TryGetUnquotedLength(&input[1],{{input.Length}},ref needed)||needed!=0)return 6;
                if(JsonString::TryUnquote(&input[1],{{input.Length}},&output[1],254,ref written)||written!=0)return 7;
                for(nuint i=0;i<256;i++)if(output[i]!='Z')return 8;
                written=999;
                if(JsonString::TryUnquote(&input[1],{{input.Length}},&input[1],254,ref written)||written!=0)return 9;
                for(nuint i=1;i<{{input.Length + 1}};i++)if(input[i]!=original[i])return 10;
                if(input[0]!='!'||input[{{input.Length + 1}}]!='?')return 11;
                """);
        }
        code.AppendLine("char*? absent=null; needed=999; written=999;\n");
        code.AppendLine("if(JsonString::TryGetUnquotedLength(absent!,0,ref needed)||needed!=0)return 12;");
        code.AppendLine("if(JsonString::TryUnquote(absent!,0,absent!,0,ref written)||written!=0)return 13;");
        code.AppendLine("if(!JsonString::TryUnquote(\"\\\"\\\"\",2,absent!,0,ref written)||written!=0)return 14;");
        code.AppendLine("return 0; }");
        Run(code.ToString(), optimization);
    }
}
