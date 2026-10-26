using System.Text;

namespace gflat.Tests;

public class StandardUtf8AliasTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DecodingCapturesInputBeforeStoringEitherOverlappingResult(int optimization)
    {
        uint[] scalars = [0u, 65u, 0x7fu, 0x80u, 0x7ffu, 0x800u, 0xd7ffu, 0xe000u, 0xffffu, 0x10000u, 0x1f600u, 0x10ffffu];
        string cases = string.Join("\n", scalars.Select(value =>
        {
            Span<byte> buffer = stackalloc byte[4];
            int count = new Rune((int)value).EncodeToUtf8(buffer);
            byte[] bytes = buffer[..count].ToArray();
            string fill = string.Join(" ", bytes.Select((b, index) => $"input[{index}]=(char){b};"));
            return $$"""
                {
                    // Aligned scalar output overlaps the bytes being decoded.
                    uint storage = 0u; char* input = (char*)&storage;
                    {{fill}}
                    nuint consumed = 99;
                    if (!std::Utf8::TryDecode(input, {{count}}, &storage, &consumed) || storage != {{value}}u || consumed != (nuint){{count}}) return 1;
                }
                {
                    // Aligned count output overlaps input, independently of scalar.
                    nuint[4] storage; char* input = (char*)&storage[0];
                    {{fill}}
                    uint scalar = 99u;
                    if (!std::Utf8::TryDecode(input, {{count}}, &scalar, &storage[0]) || scalar != {{value}}u || storage[0] != (nuint){{count}}) return 2;
                }
                """;
        }));
        Run($$"""
            int main() {
                {{cases}}
                uint storage = 0u; char* input = (char*)&storage;
                input[0]=(char)0xe0; input[1]=(char)0x80; input[2]=(char)0x80;
                nuint consumed = 99;
                if (std::Utf8::TryDecode(input,3,&storage,&consumed) || storage != 0u || consumed != (nuint)0) return 3;
                nuint other = 0; input=(char*)&other;
                input[0]=(char)0xf0; input[1]=(char)0x90;
                uint scalar = 99u;
                if (std::Utf8::TryDecode(input,2,&scalar,&other) || scalar != 0u || other != (nuint)0) return 4;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountingValidatesEntireOverlappingInputBeforeWritingItsResult(int optimization)
        => Run("""
            int main() {
                nuint[4] storage;
                char* text=(char*)&storage[0];
                text[0]='A'; text[1]=(char)0; text[2]=(char)0xe2; text[3]=(char)0x82; text[4]=(char)0xac;
                text[5]=(char)0xf0; text[6]=(char)0x9f; text[7]=(char)0x98; text[8]=(char)0x80;
                if (!std::Utf8::TryCount(text,9,&storage[0]) || storage[0]!=(nuint)4) return 1;
                text[0]='A'; text[1]=(char)0xe0; text[2]=(char)0x80; text[3]=(char)0x80;
                if (std::Utf8::TryCount(text,4,&storage[0]) || storage[0]!=(nuint)0) return 2;
                text[0]=(char)0xf0; text[1]=(char)0x90;
                if (std::Utf8::TryCount(text,2,&storage[0]) || storage[0]!=(nuint)0) return 3;
                if (!std::Utf8::TryCount(text,0,&storage[0]) || storage[0]!=(nuint)0) return 4;
                return 0;
            }
            """, optimization);

    private static void Run(string code, int optimization)
    {
        SourceFile[] sources = [new("Utf8.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Std/core/Text/utf8.gf"))), new("main.gf", code)];
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true });
        Assert.DoesNotContain("@__gflat_eh_", ir);
        Assert.DoesNotContain("personality", ir);
        Assert.Equal(0, CompilerTestHelper.Run(sources, optimization).ExitCode);
    }
}
