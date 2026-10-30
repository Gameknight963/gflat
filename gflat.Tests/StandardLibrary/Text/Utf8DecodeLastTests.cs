using System.Buffers;
using System.Text;

namespace gflat.Tests.Std.Text;

public sealed class Utf8DecodeLastTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FinalScalarsAndMalformedSuffixesMatchDotNetWithoutValidatingThePrefix(int optimization)
    {
        uint[] scalars = [0u, 0x7fu, 0x80u, 0x7ffu, 0x800u, 0xd7ffu,
            0xe000u, 0xffffu, 0x10000u, 0x1f600u, 0x10ffffu];
        var cases = new List<byte[]>();
        foreach (uint scalar in scalars)
        {
            byte[] encoded = Encoding.UTF8.GetBytes(new Rune((int)scalar).ToString());
            cases.Add(encoded);
            cases.Add(new byte[] { 0xff, 0x80, 0, 0xc2 }.Concat(encoded).ToArray());
            for (int length = 1; length < encoded.Length; length++)
                cases.Add(encoded[..length]);
        }
        foreach (string hex in new[] { "", "80", "BF", "C0AF", "C180", "C241", "E08080",
            "E19F", "E1FF80", "EDA080", "EDBFBF", "F0808080", "F4908080", "F5808080",
            "FFBFBFBF", "8080808080", "4180", "F09F988080", "E282AC00", "FF41" })
            cases.Add(Convert.FromHexString(hex));
        var random = new Random(586219);
        for (int index = 0; index < 64; index++)
        {
            byte[] bytes = new byte[index % 9];
            random.NextBytes(bytes);
            cases.Add(bytes);
        }

        var checks = new StringBuilder();
        foreach (byte[] bytes in cases)
        {
            bool valid = Rune.DecodeLastFromUtf8(bytes, out Rune rune, out int consumed) == OperationStatus.Done;
            checks.AppendLine($$"""
                {
                    char[{{bytes.Length + 2}}] storage = [{{Bytes([0x5a, ..bytes, 0x5a])}}];
                    uint scalar = 99u;
                    nuint consumed = 99;
                    bool valid = Standard::Text::Utf8::TryDecodeLast(&storage[1], {{bytes.Length}}, &scalar, &consumed);
                    if (valid != {{(valid ? "true" : "false")}}) return 1;
                    if (scalar != {{(valid ? (uint)rune.Value : 0u)}}u || consumed != (nuint){{(valid ? consumed : 0)}}) return 2;
                    char[{{bytes.Length + 2}}] expected = [{{Bytes([0x5a, ..bytes, 0x5a])}}];
                    for (nuint i = 0; i < {{bytes.Length + 2}}; i++) if (storage[i] != expected[i]) return 3;
                }
                """);
        }
        Run("int main(){" + checks + "return 0;}", optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RepeatedDecodingWalksMixedScalarBoundariesBackward(int optimization)
    {
        const string text = "A\0\u007f\u0080\u07ff\u0800\ud7ff\ue000\uffff\U00010000\U0001f600\U0010ffffZ";
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        Rune[] expected = text.EnumerateRunes().Reverse().ToArray();
        var checks = new StringBuilder();
        foreach (Rune rune in expected)
        {
            checks.AppendLine($$"""
                if (!Standard::Text::Utf8::TryDecodeLast(input, length, &scalar, &consumed)) return 1;
                if (scalar != {{rune.Value}}u || consumed != (nuint){{rune.Utf8SequenceLength}}) return 2;
                length -= consumed;
                """);
        }
        Run($$"""
            int main(){
                char[{{bytes.Length}}] input = [{{Bytes(bytes)}}];
                nuint length = {{bytes.Length}};
                uint scalar = 0u;
                nuint consumed = 0;
                {{checks}}
                if (length != (nuint)0) return 3;
                if (Standard::Text::Utf8::TryDecodeLast(((nuint)1).reinterpret<readonly(char)*>(), 0, &scalar, &consumed)) return 4;
                if (scalar != 0u || consumed != (nuint)0) return 5;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OverlappingResultsAreStoredOnlyAfterTheFinalScalarWasRead(int optimization)
    {
        uint[] scalars = [0u, 0x7fu, 0x80u, 0x7ffu, 0x800u, 0xd7ffu,
            0xe000u, 0xffffu, 0x10000u, 0x1f600u, 0x10ffffu];
        var checks = new StringBuilder();
        foreach (uint scalar in scalars)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(new Rune((int)scalar).ToString());
            string initialize = string.Join(" ", bytes.Select((value, index) => $"input[{index}] = (char){value};"));
            checks.AppendLine($$"""
                {
                    uint[3] storage = [165u, 0u, 165u];
                    char* input = (&storage[1]).reinterpret<char*>();
                    {{initialize}}
                    nuint consumed = 99;
                    if (!Standard::Text::Utf8::TryDecodeLast(input, {{bytes.Length}}, &storage[1], &consumed)) return 1;
                    if (storage[1] != {{scalar}}u || consumed != (nuint){{bytes.Length}}) return 2;
                    if (storage[0] != 165u || storage[2] != 165u) return 3;
                }
                {
                    nuint[3] storage = [165, 0, 165];
                    char* input = (&storage[1]).reinterpret<char*>();
                    {{initialize}}
                    uint scalar = 99u;
                    if (!Standard::Text::Utf8::TryDecodeLast(input, {{bytes.Length}}, &scalar, &storage[1])) return 4;
                    if (scalar != {{scalar}}u || storage[1] != (nuint){{bytes.Length}}) return 5;
                    if (storage[0] != (nuint)165 || storage[2] != (nuint)165) return 6;
                }
                """);
        }
        Run($$"""
            int main(){
                {{checks}}
                uint[3] scalarStorage = [165u, 0u, 165u];
                char* input = (&scalarStorage[1]).reinterpret<char*>();
                input[0] = (char)0xe0; input[1] = (char)0x80; input[2] = (char)0x80;
                nuint consumed = 99;
                if (Standard::Text::Utf8::TryDecodeLast(input, 3, &scalarStorage[1], &consumed)) return 7;
                if (scalarStorage[1] != 0u || consumed != (nuint)0) return 8;
                if (scalarStorage[0] != 165u || scalarStorage[2] != 165u) return 9;
                nuint[3] countStorage = [165, 0, 165];
                input = (&countStorage[1]).reinterpret<char*>();
                input[0] = (char)0xf0; input[1] = (char)0x90;
                uint scalar = 99u;
                if (Standard::Text::Utf8::TryDecodeLast(input, 2, &scalar, &countStorage[1])) return 10;
                if (scalar != 0u || countStorage[1] != (nuint)0) return 11;
                if (countStorage[0] != (nuint)165 || countStorage[2] != (nuint)165) return 12;
                return 0;
            }
            """, optimization);
    }

    private static string Bytes(byte[] bytes)
        => string.Join(", ", bytes.Select(value => $"(char){value}"));

    private static void Run(string code, int optimization)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Standard", "core", "Text", "utf8.gf");
        SourceFile[] sources = [new(path, File.ReadAllText(path)), new("utf8-decode-last.gf", code)];
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true });
        Assert.DoesNotContain("@__gflat_eh_", ir);
        Assert.DoesNotContain("personality", ir);
        Assert.DoesNotContain("@malloc", ir);
        Assert.DoesNotContain("gflat$Memory$Allocate", ir);
        var result = CompilerTestHelper.RunIr(ir, optimization);
        Assert.True(result.ExitCode == 0, $"UTF-8 final decode exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
