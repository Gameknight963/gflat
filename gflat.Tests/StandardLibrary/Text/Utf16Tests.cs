using System.Text;

namespace gflat.Tests.Std.Text;

public sealed class Utf16Tests
{
    private static void Run(string program, int optimization)
    {
        SourceFile[] sources = [new("Utf16.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard/core/Text/utf16.gf"))), new("main.gf", program)];
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, InternalizeFunctions = true });
        Assert.DoesNotContain("__gflat_eh", ir);
        Assert.DoesNotContain("@malloc", ir);
        string directory = Path.Combine(Path.GetTempPath(), "gflat-utf16-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "program.ll"), output = Path.Combine(directory, "program.exe");
            File.WriteAllText(input, ir);
            var compiled = NativeToolchain.Compile(input, output, optimizationLevel: optimization);
            Assert.True(compiled.ExitCode == 0, compiled.StandardError);
            var result = NativeToolchain.Run(output, [], 30000);
            Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BoundaryScalarsMatchDotNetRuneWithNoPartialWrites(int optimization)
    {
        uint[] values = [0, 0x41, 0x7f, 0x80, 0x7ff, 0x800, 0xd7ff, 0xe000, 0xffff, 0x10000, 0x103ff, 0x10400, 0x1f600, 0x10ffff];
        var code = new StringBuilder("int main() nothrow { ushort[4] units; nuint written = 99; uint scalar = 0u; nuint consumed = 99;\n");
        foreach (uint value in values)
        {
            char[] expected = new char[2];
            int count = new Rune((int)value).EncodeToUtf16(expected);
            code.AppendLine($$"""
                units[0] = (ushort)123; units[1] = (ushort)123; units[2] = (ushort)123; units[3] = (ushort)123;
                if (Standard::Text::Utf16::EncodedLength({{value}}u) != (nuint){{count}}) return 1;
                for (nuint capacity = 0; capacity < (nuint){{count}}; capacity++) {
                    written = 99;
                    if (Standard::Text::Utf16::TryEncode({{value}}u, &units[1], capacity, &written) || written != 0) return 2;
                    for (int i = 0; i < 4; i++) if (units[i] != (ushort)123) return 3;
                }
                if (!Standard::Text::Utf16::TryEncode({{value}}u, &units[1], {{count}}, &written) || written != (nuint){{count}}) return 4;
                if (units[0] != (ushort)123 || units[{{count + 1}}] != (ushort)123) return 5;
                """);
            for (int i = 0; i < count; i++) code.AppendLine($"if (units[{i + 1}] != (ushort){(int)expected[i]}) return 6;");
            code.AppendLine($$"""
                for (nuint available = 0; available < written; available++) {
                    scalar = 99u; consumed = 99;
                    if (Standard::Text::Utf16::TryDecode(&units[1], available, &scalar, &consumed) || scalar != 0u || consumed != 0) return 7;
                }
                if (!Standard::Text::Utf16::TryDecode(&units[1], written, &scalar, &consumed) || scalar != {{value}}u || consumed != written) return 8;
                """);
        }
        code.AppendLine("return 0; }");
        Run(code.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryScalarRoundTripsAndEveryIsolatedSurrogateFails(int optimization)
    {
        Run("""
            int main() nothrow {
                ushort[2] units; nuint written = 0; uint scalar = 0u; nuint consumed = 0;
                for (uint value = 0u; value <= 0x10ffffu; value++) {
                    bool valid = !(value >= 0xd800u && value <= 0xdfffu);
                    if (Standard::Text::Utf16::IsValidScalar(value) != valid) return 1;
                    units[0] = (ushort)42; units[1] = (ushort)43;
                    written = 99;
                    if (Standard::Text::Utf16::TryEncode(value, units, 2, &written) != valid) return 2;
                    if (!valid) {
                        if (written != 0 || units[0] != (ushort)42 || units[1] != (ushort)43) return 3;
                        units[0] = (ushort)value; scalar = 99u; consumed = 99;
                        if (Standard::Text::Utf16::TryDecode(units, 1, &scalar, &consumed) || scalar != 0u || consumed != 0) return 4;
                        continue;
                    }
                    if (!Standard::Text::Utf16::TryDecode(units, written, &scalar, &consumed) || scalar != value || consumed != written) return 5;
                }
                if (Standard::Text::Utf16::EncodedLength(0x110000u) != 0 || Standard::Text::Utf16::IsValidScalar(0xffffffffu)) return 6;
                units[0] = (ushort)42; units[1] = (ushort)43;
                if (Standard::Text::Utf16::TryEncode(0xffffffffu, units, 2, &written) || written != 0 || units[0] != (ushort)42 || units[1] != (ushort)43) return 7;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountsNullScalarsRejectsMalformedSuffixesAndAcceptsNoncharacters(int optimization)
    {
        ushort[][] invalid = [[0xd800], [0xdbff], [0xdc00], [0xdfff], [0xd800, 0x41], [0xd800, 0xd800], [0xdbff, 0xffff], [0xdc00, 0xd800]];
        var code = new StringBuilder("int main() nothrow { ushort[5] units; uint scalar = 99u; nuint consumed = 99; nuint count = 99;\n");
        foreach (ushort[] input in invalid)
        {
            for (int i = 0; i < input.Length; i++) code.AppendLine($"units[{i}] = (ushort){input[i]};");
            code.AppendLine($$"""
                scalar = 99u; consumed = 99; count = 99;
                if (Standard::Text::Utf16::TryDecode(units, {{input.Length}}, &scalar, &consumed) || scalar != 0u || consumed != 0) return 1;
                if (Standard::Text::Utf16::IsValid(units, {{input.Length}})) return 2;
                if (Standard::Text::Utf16::TryCount(units, {{input.Length}}, &count) || count != 0) return 3;
                """);
        }
        code.AppendLine("""
            units[0] = (ushort)0; units[1] = (ushort)0xd83d; units[2] = (ushort)0xde00; units[3] = (ushort)0xffff; units[4] = (ushort)0xd800;
            if (!Standard::Text::Utf16::TryCount(units, 4, &count) || count != 3 || !Standard::Text::Utf16::IsValid(units, 4)) return 4;
            if (Standard::Text::Utf16::TryCount(units, 5, &count) || count != 0) return 5;
            readonly(ushort)*? absent = null; readonly(ushort)* empty = absent!;
            if (!Standard::Text::Utf16::TryCount(empty, 0, &count) || count != 0 || !Standard::Text::Utf16::IsValid(empty, 0)) return 6;
            if (Standard::Text::Utf16::TryDecode(empty, 0, &scalar, &consumed) || scalar != 0u || consumed != 0) return 7;
            ushort*? noOutput = null;
            if (Standard::Text::Utf16::TryEncode(65u, noOutput!, 0, &count) || count != 0) return 8;
            return 0;
            }
            """);
        Run(code.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DecodeAndCountOutputsMayOverlapInputAfterAllRequiredReads(int optimization)
    {
        Run("""
            int main() nothrow {
                ulong[4] aligned;
                ushort* units = ((ulong*)aligned).reinterpret<ushort*>();
                uint* scalar = (&aligned[0]).reinterpret<uint*>();
                nuint* count = (&aligned[1]).reinterpret<nuint*>();
                nuint consumed = 99; uint decoded = 99u;
                units[0] = (ushort)0xd83d; units[1] = (ushort)0xde00;
                if (!Standard::Text::Utf16::TryDecode(units, 2, scalar, &consumed) || *scalar != 0x1f600u || consumed != 2) return 1;
                units[4] = (ushort)0xdbff; units[5] = (ushort)0xdfff;
                if (!Standard::Text::Utf16::TryDecode(&units[4], 2, &decoded, count) || decoded != 0x10ffffu || *count != 2) return 2;
                units[4] = (ushort)65; units[5] = (ushort)0xd83d; units[6] = (ushort)0xde00; units[7] = (ushort)0;
                if (!Standard::Text::Utf16::TryCount(&units[4], 4, count) || *count != 3) return 3;
                units[4] = (ushort)65; units[5] = (ushort)0xd83d; units[6] = (ushort)0xde00; units[7] = (ushort)0xd800;
                if (Standard::Text::Utf16::TryCount(&units[4], 4, count) || *count != 0) return 4;
                units[0] = (ushort)0xd800; units[1] = (ushort)65;
                if (Standard::Text::Utf16::TryDecode(units, 2, scalar, &consumed) || *scalar != 0u || consumed != 0) return 5;
                return 0;
            }
            """, optimization);
    }
}
