using System.Text;

namespace gflat.Tests;

public class StandardUtf8Tests
{
    [Fact]
    public void CoreCodecCompilesWithoutStandardLibraryOrRuntimeDefinitions()
    {
        var codec = new SourceFile("Utf8.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Text/utf8.gf")));
        Compiler.Emit([codec, new SourceFile("main.gf", "int main() => (int)std::Utf8::EncodedLength(65u);")], options: new() { NoStdLib = true });
    }

    private static void Run(string code)
    {
        var source = new SourceFile("Utf8.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/Text/utf8.gf")));
        Assert.Equal(0, CompilerTestHelper.Run([source, new SourceFile("main.gf", code)]).ExitCode);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(0x7fu)]
    [InlineData(0x80u)]
    [InlineData(0x7ffu)]
    [InlineData(0x800u)]
    [InlineData(0xd7ffu)]
    [InlineData(0xe000u)]
    [InlineData(0xffffu)]
    [InlineData(0x10000u)]
    [InlineData(0x1f600u)]
    [InlineData(0x10ffffu)]
    public void ScalarBoundaryEncodingMatchesDotNetAndFailureNeverPartiallyWrites(uint value)
    {
        Span<byte> expected = stackalloc byte[4];
        int length = new Rune((int)value).EncodeToUtf8(expected);
        byte[] encoded = expected[..length].ToArray();
        string checks = string.Join("\n", Enumerable.Range(0, length).Select(i => $"if ((byte)bytes[{i}] != (byte){encoded[i]}) return 10;"));
        Run($$"""
            int main() {
                char[5] bytes = ['Z','Z','Z','Z','Z'];
                nuint written = 99;
                if (std::Utf8::EncodedLength({{value}}u) != (nuint){{length}}) return 1;
                for (nuint capacity = 0; capacity < (nuint){{length}}; capacity++) {
                    if (std::Utf8::TryEncode({{value}}u, bytes, capacity, &written) || written != (nuint)0) return 2;
                    for (int j = 0; j < 5; j++) if (bytes[j] != 'Z') return 3;
                }
                if (!std::Utf8::TryEncode({{value}}u, bytes, 5, &written) || written != (nuint){{length}}) return 4;
                {{checks}}
                if (bytes[{{length}}] != 'Z') return 5;
                uint scalar = 123u; nuint consumed = 99;
                for (nuint count = 0; count < written; count++) {
                    if (std::Utf8::TryDecode(bytes, count, &scalar, &consumed) || scalar != 0u || consumed != (nuint)0) return 6;
                }
                if (!std::Utf8::TryDecode(bytes, written, &scalar, &consumed) || scalar != {{value}}u || consumed != written) return 7;
                return 0;
            }
            """);
    }

    [Theory]
    [InlineData("80")]
    [InlineData("BF")]
    [InlineData("C0AF")]
    [InlineData("C180")]
    [InlineData("C2")]
    [InlineData("C241")]
    [InlineData("E08080")]
    [InlineData("E19F")]
    [InlineData("E1FF80")]
    [InlineData("EDA080")]
    [InlineData("EDBFBF")]
    [InlineData("F0808080")]
    [InlineData("F4908080")]
    [InlineData("F5808080")]
    [InlineData("FFBFBFBF")]
    public void MalformedSequencesAreRejectedWithClearedOutputs(string hex)
    {
        byte[] bytes = Convert.FromHexString(hex);
        string initialization = string.Join(",", bytes.Select(b => $"(char){b}"));
        Run($$"""
            int main() {
                char[{{bytes.Length}}] bytes = [{{initialization}}];
                uint scalar = 42u; nuint consumed = 42; nuint count = 42;
                if (std::Utf8::TryDecode(bytes, {{bytes.Length}}, &scalar, &consumed)) return 1;
                if (scalar != 0u || consumed != (nuint)0) return 2;
                if (std::Utf8::IsValid(bytes, {{bytes.Length}})) return 3;
                if (std::Utf8::TryCount(bytes, {{bytes.Length}}, &count) || count != (nuint)0) return 4;
                return 0;
            }
            """);
    }

    [Fact]
    public void EveryUnicodeScalarRoundTripsAndSurrogatesNeverEncode()
        => Run("""
            int main() {
                char[4] bytes;
                nuint written = 7;
                bool valid = false;
                uint decoded = 0u;
                nuint consumed = 0;
                for (uint value = 0u; value <= 0x10ffffu; value++) {
                    written = 7;
                    valid = std::Utf8::IsValidScalar(value);
                    if (std::Utf8::TryEncode(value, bytes, 4, &written) != valid) return 1;
                    if (!valid) { if (written != (nuint)0) return 2; continue; }
                    decoded = 0u; consumed = 0;
                    if (!std::Utf8::TryDecode(bytes, written, &decoded, &consumed)) return 3;
                    if (decoded != value) return 4;
                    if (consumed != written) return 4;
                }
                if (std::Utf8::IsValidScalar(0x110000u) || std::Utf8::IsValidScalar(0xffffffffu)) return 5;
                written = 1;
                if (std::Utf8::TryEncode(0xffffffffu, bytes, 4, &written) || written != (nuint)0) return 6;
                return 0;
            }
            """);

    [Fact]
    public void CountIncludesNullScalarsAndRejectsInvalidSuffixes()
        => Run("""
            int main() {
                char[10] text = ['A','\0',(char)0xe2,(char)0x82,(char)0xac,(char)0xf0,(char)0x9f,(char)0x98,(char)0x80,(char)0xff];
                nuint count = 99;
                if (!std::Utf8::TryCount(text, 9, &count) || count != (nuint)4) return 1;
                if (std::Utf8::TryCount(text, 10, &count) || count != (nuint)0) return 2;
                if (!std::Utf8::TryCount(text, 0, &count) || count != (nuint)0 || !std::Utf8::IsValid(text, 0)) return 3;
                uint scalar = 0u; nuint consumed = 0;
                if (!std::Utf8::TryDecode(text, 10, &scalar, &consumed) || scalar != 65u || consumed != (nuint)1) return 4;
                return 0;
            }
            """);

    [Fact]
    public void LongAsciiInputValidatesWithoutAccumulatingStackStorage()
        => Run("""
            int main() {
                char[131072] text;
                for (nuint i = 0; i < (nuint)131072; i++) text[i] = 'a';
                nuint count = 0;
                if (!std::Utf8::TryCount(text, 131072, &count)) return 1;
                if (count != (nuint)131072) return 2;
                return 0;
            }
            """);
}
