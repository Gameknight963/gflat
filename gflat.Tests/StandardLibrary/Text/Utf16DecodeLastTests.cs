using System.Buffers;
using System.Text;

namespace gflat.Tests.Std.Text;

public sealed class Utf16DecodeLastTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FinalScalarsAndMalformedSuffixesMatchDotNetWithoutValidatingThePrefix(int optimization)
    {
        uint[] scalars = [0u, 0x41u, 0x7fu, 0x80u, 0x7ffu, 0x800u, 0xd7ffu,
            0xe000u, 0xfeffu, 0xffffu, 0x10000u, 0x103ffu, 0x10400u, 0x1f600u, 0x10ffffu];
        var cases = new List<ushort[]>();
        foreach (uint scalar in scalars)
        {
            ushort[] units = new Rune((int)scalar).ToString().Select(character => (ushort)character).ToArray();
            cases.Add(units);
            cases.Add(new ushort[] { 0xdc00, 0, 0xd800 }.Concat(units).ToArray());
            if (units.Length == 2) cases.Add(units[..1]);
        }
        cases.AddRange(new ushort[][] { [], [0xd800], [0xdbff], [0xdc00], [0xdfff],
            [0xd800, 0x41], [0xd800, 0xd800], [0xdc00, 0xdc00], [0x41, 0xdc00],
            [0xdc00, 0xd800], [0xd83d, 0xde00, 0xdc00], [0xd800, 0], [0xdfff, 0xfeff] });
        var random = new Random(586216);
        for (int index = 0; index < 64; index++)
        {
            ushort[] units = new ushort[index % 6];
            for (int unit = 0; unit < units.Length; unit++) units[unit] = (ushort)random.Next(0x10000);
            cases.Add(units);
        }

        var checks = new StringBuilder();
        foreach (ushort[] units in cases)
        {
            char[] input = units.Select(unit => (char)unit).ToArray();
            bool valid = Rune.DecodeLastFromUtf16(input, out Rune rune, out int consumed) == OperationStatus.Done;
            checks.AppendLine($$"""
                {
                    ushort[{{units.Length + 2}}] storage = [{{Units([0xa5a5, ..units, 0xa5a5])}}];
                    uint scalar = 99u;
                    nuint consumed = 99;
                    bool valid = Standard::Text::Utf16::TryDecodeLast(&storage[1], {{units.Length}}, &scalar, &consumed);
                    if (valid != {{(valid ? "true" : "false")}}) return 1;
                    if (scalar != {{(valid ? (uint)rune.Value : 0u)}}u || consumed != (nuint){{(valid ? consumed : 0)}}) return 2;
                    ushort[{{units.Length + 2}}] expected = [{{Units([0xa5a5, ..units, 0xa5a5])}}];
                    for (nuint i = 0; i < {{units.Length + 2}}; i++) if (storage[i] != expected[i]) return 3;
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
        const string text = "A\0\u007f\u0080\u07ff\u0800\ud7ff\ue000\ufeff\uffff\U00010000\U0001f600\U0010ffffZ";
        ushort[] units = text.Select(character => (ushort)character).ToArray();
        Rune[] expected = text.EnumerateRunes().Reverse().ToArray();
        var checks = new StringBuilder();
        foreach (Rune rune in expected)
        {
            checks.AppendLine($$"""
                if (!Standard::Text::Utf16::TryDecodeLast(input, length, &scalar, &consumed)) return 1;
                if (scalar != {{rune.Value}}u || consumed != (nuint){{rune.Utf16SequenceLength}}) return 2;
                length -= consumed;
                """);
        }
        Run($$"""
            int main(){
                ushort[{{units.Length}}] input = [{{Units(units)}}];
                nuint length = {{units.Length}};
                uint scalar = 0u;
                nuint consumed = 0;
                {{checks}}
                if (length != (nuint)0) return 3;
                if (Standard::Text::Utf16::TryDecodeLast(((nuint)1).reinterpret<readonly(ushort)*>(), 0, &scalar, &consumed)) return 4;
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
        uint[] scalars = [0u, 0x41u, 0xd7ffu, 0xe000u, 0xfeffu, 0xffffu, 0x10000u, 0x1f600u, 0x10ffffu];
        var checks = new StringBuilder();
        foreach (uint scalar in scalars)
        {
            ushort[] units = new Rune((int)scalar).ToString().Select(character => (ushort)character).ToArray();
            string initialize = string.Join(" ", units.Select((value, index) => $"input[{index}] = (ushort){value};"));
            checks.AppendLine($$"""
                {
                    uint[3] storage = [165u, 0u, 165u];
                    ushort* input = (&storage[1]).reinterpret<ushort*>();
                    {{initialize}}
                    nuint consumed = 99;
                    if (!Standard::Text::Utf16::TryDecodeLast(input, {{units.Length}}, &storage[1], &consumed)) return 1;
                    if (storage[1] != {{scalar}}u || consumed != (nuint){{units.Length}}) return 2;
                    if (storage[0] != 165u || storage[2] != 165u) return 3;
                }
                {
                    nuint[3] storage = [165, 0, 165];
                    ushort* input = (&storage[1]).reinterpret<ushort*>();
                    {{initialize}}
                    uint scalar = 99u;
                    if (!Standard::Text::Utf16::TryDecodeLast(input, {{units.Length}}, &scalar, &storage[1])) return 4;
                    if (scalar != {{scalar}}u || storage[1] != (nuint){{units.Length}}) return 5;
                    if (storage[0] != (nuint)165 || storage[2] != (nuint)165) return 6;
                }
                """);
        }
        Run($$"""
            int main(){
                {{checks}}
                uint[3] scalarStorage = [165u, 0u, 165u];
                ushort* input = (&scalarStorage[1]).reinterpret<ushort*>();
                input[0] = (ushort)65; input[1] = (ushort)0xdc00;
                nuint consumed = 99;
                if (Standard::Text::Utf16::TryDecodeLast(input, 2, &scalarStorage[1], &consumed)) return 7;
                if (scalarStorage[1] != 0u || consumed != (nuint)0) return 8;
                if (scalarStorage[0] != 165u || scalarStorage[2] != 165u) return 9;
                nuint[3] countStorage = [165, 0, 165];
                input = (&countStorage[1]).reinterpret<ushort*>();
                input[0] = (ushort)0xd800; input[1] = (ushort)0xd800;
                uint scalar = 99u;
                if (Standard::Text::Utf16::TryDecodeLast(input, 2, &scalar, &countStorage[1])) return 10;
                if (scalar != 0u || countStorage[1] != (nuint)0) return 11;
                if (countStorage[0] != (nuint)165 || countStorage[2] != (nuint)165) return 12;
                return 0;
            }
            """, optimization);
    }

    private static string Units(ushort[] units)
        => string.Join(", ", units.Select(value => $"(ushort){value}"));

    private static void Run(string code, int optimization)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Standard", "core", "Text", "utf16.gf");
        SourceFile[] sources = [new(path, File.ReadAllText(path)), new("utf16-decode-last.gf", code)];
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true });
        Assert.DoesNotContain("@__gflat_eh_", ir);
        Assert.DoesNotContain("personality", ir);
        Assert.DoesNotContain("@malloc", ir);
        Assert.DoesNotContain("gflat$Memory$Allocate", ir);
        var result = CompilerTestHelper.RunIr(ir, optimization);
        Assert.True(result.ExitCode == 0, $"UTF-16 final decode exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
