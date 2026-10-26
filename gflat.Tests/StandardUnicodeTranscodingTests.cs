using System.Text;

namespace gflat.Tests;

public sealed class StandardUnicodeTranscodingTests
{
    private static string Bytes(IEnumerable<byte> bytes) => string.Join(",", bytes.Select(value => $"(char){value}"));
    private static string Units(IEnumerable<char> units) => string.Join(",", units.Select(value => $"(ushort){(int)value}"));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BothDirectionsMatchStrictDotNetEncodingAndPreserveCapacityGuards(int optimization)
    {
        var texts = new List<string> { "", "\0", "a\0b", "hello", "\u007f\u0080\u07ff\u0800\uffff", "\ufeffabc", "\ud800\udc00\udbff\udfff" };
        var random = new Random(7891);
        for (int sample = 0; sample < 24; sample++)
        {
            var text = new StringBuilder();
            for (int i = 0; i < sample * 2; i++)
            {
                int scalar;
                do scalar = random.Next(0x110000); while (scalar is >= 0xd800 and <= 0xdfff);
                text.Append(char.ConvertFromUtf32(scalar));
            }
            texts.Add(text.ToString());
        }
        var program = new StringBuilder("using std; int main(){");
        foreach (string text in texts)
        {
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(text);
            program.AppendLine($$"""
                {
                    char[{{bytes.Length + 1}}] input=[{{Bytes(bytes.Append((byte)90))}}];
                    ushort[{{text.Length + 1}}] expected=[{{Units(text.Append('Z'))}}];
                    ushort[{{text.Length + 2}}] wide;
                    char[{{bytes.Length + 2}}] output;
                    for(int i=0;i<{{text.Length + 2}};i++)wide[i]=(ushort)43690;
                    for(int i=0;i<{{bytes.Length + 2}};i++)output[i]=(char)165;
                    nuint needed=999; nuint written=999;
                    if(!UnicodeTranscoding::TryGetUtf16Length(input,{{bytes.Length}},ref needed)||needed!={{text.Length}})return 1;
                    if(!UnicodeTranscoding::TryGetUtf8Length(expected,{{text.Length}},ref needed)||needed!={{bytes.Length}})return 2;
                """);
            if (text.Length > 0)
                program.AppendLine($$"""
                    if(UnicodeTranscoding::TryUtf8ToUtf16(input,{{bytes.Length}},&wide[1],{{text.Length - 1}},ref written)||written!=0)return 3;
                    for(int i=0;i<{{text.Length + 2}};i++)if(wide[i]!=(ushort)43690)return 4;
                """);
            if (bytes.Length > 0)
                program.AppendLine($$"""
                    if(UnicodeTranscoding::TryUtf16ToUtf8(expected,{{text.Length}},&output[1],{{bytes.Length - 1}},ref written)||written!=0)return 5;
                    for(int i=0;i<{{bytes.Length + 2}};i++)if((byte)output[i]!=(byte)165)return 6;
                """);
            program.AppendLine($$"""
                    if(!UnicodeTranscoding::TryUtf8ToUtf16(input,{{bytes.Length}},&wide[1],{{text.Length}},ref written)||written!={{text.Length}})return 7;
                    for(int i=0;i<{{text.Length}};i++)if(wide[i+1]!=expected[i])return 8;
                    if(wide[0]!=(ushort)43690||wide[{{text.Length + 1}}]!=(ushort)43690)return 9;
                    if(!UnicodeTranscoding::TryUtf16ToUtf8(&wide[1],{{text.Length}},&output[1],{{bytes.Length}},ref written)||written!={{bytes.Length}})return 10;
                    for(int i=0;i<{{bytes.Length}};i++)if(output[i+1]!=input[i])return 11;
                    if((byte)output[0]!=(byte)165||(byte)output[{{bytes.Length + 1}}]!=(byte)165)return 12;
                }
                """);
        }
        program.Append("return 0;}");
        Run(program.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MalformedSuffixesAreRejectedBeforeWritingAnyConvertedPrefix(int optimization)
    {
        byte[][] invalidUtf8 = [[65, 0x80], [65, 0xc0, 0x80], [65, 0xf0, 0x90, 0x80], [65, 0xed, 0xa0, 0x80], [65, 0xf4, 0x90, 0x80, 0x80]];
        char[][] invalidUtf16 = [['A', '\ud800'], ['A', '\udc00'], ['A', '\ud800', 'B'], ['A', '\udbff', '\ud800']];
        var program = new StringBuilder("using std; int main(){nuint length=99; nuint written=99; ushort[16] wide; char[16] bytes; for(int i=0;i<16;i++){wide[i]=(ushort)43690; bytes[i]=(char)165;}");
        foreach (byte[] input in invalidUtf8)
            program.AppendLine($$"""
                { char[{{input.Length}}] input=[{{Bytes(input)}}];
                  if(UnicodeTranscoding::TryGetUtf16Length(input,{{input.Length}},ref length)||length!=0)return 1;
                  if(UnicodeTranscoding::TryUtf8ToUtf16(input,{{input.Length}},wide,16,ref written)||written!=0)return 2;
                  for(int i=0;i<16;i++)if(wide[i]!=(ushort)43690)return 3; }
                """);
        foreach (char[] input in invalidUtf16)
            program.AppendLine($$"""
                { ushort[{{input.Length}}] input=[{{Units(input)}}];
                  if(UnicodeTranscoding::TryGetUtf8Length(input,{{input.Length}},ref length)||length!=0)return 4;
                  if(UnicodeTranscoding::TryUtf16ToUtf8(input,{{input.Length}},bytes,16,ref written)||written!=0)return 5;
                  for(int i=0;i<16;i++)if((byte)bytes[i]!=(byte)165)return 6; }
                """);
        program.Append("return 0;}");
        Run(program.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyBuffersNeedNoReadableOrWritableStorageAndAddNoTerminator(int optimization)
        => Run("""
            using std;
            int main() {
                nuint count=99;
                readonly(char)*? noBytes=null; readonly(ushort)*? noUnits=null;
                char*? noByteOutput=null; ushort*? noUnitOutput=null;
                if(!UnicodeTranscoding::TryGetUtf16Length(noBytes!,0,ref count)||count!=0)return 1;
                if(!UnicodeTranscoding::TryGetUtf8Length(noUnits!,0,ref count)||count!=0)return 2;
                if(!UnicodeTranscoding::TryUtf8ToUtf16(noBytes!,0,noUnitOutput!,0,ref count)||count!=0)return 3;
                if(!UnicodeTranscoding::TryUtf16ToUtf8(noUnits!,0,noByteOutput!,0,ref count)||count!=0)return 4;
                return 0;
            }
            """, optimization);

    private static void Run(string program, int optimization)
    {
        SourceFile[] files = new[] { "Text/utf8.gf", "Text/utf16.gf", "Text/UnicodeTranscoding.gf" }
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
            .Append(new SourceFile("main.gf", program)).ToArray();
        string ir = Compiler.Emit(files, options: new() { NoStdLib = true });
        foreach (string forbidden in new[] { "@__gflat_eh_", "personality", "gflat$Memory$", "@malloc", "@free" }) Assert.DoesNotContain(forbidden, ir);
        Assert.Equal(0, CompilerTestHelper.Run(files, optimization).ExitCode);
    }
}
