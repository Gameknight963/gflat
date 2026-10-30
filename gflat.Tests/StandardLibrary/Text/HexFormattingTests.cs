using System.Globalization;
using System.Text;

namespace gflat.Tests.Std.Text;

public sealed class HexFormattingTests
{
    private static ulong[] Values(bool wide)
    {
        var values = new HashSet<ulong> { 0, 1, 15, 16, 255, 256, 4095, 4096, 0x12345678, 0x7fffffff, 0x80000000, uint.MaxValue };
        if (wide) values.UnionWith([0x123456789abcdef, 0x7fffffffffffffff, 0x8000000000000000, ulong.MaxValue]);
        var random = new Random(631428);
        for (int i = 0; i < 32; i++)
        {
            byte[] bytes = new byte[8]; random.NextBytes(bytes);
            ulong value = BitConverter.ToUInt64(bytes);
            values.Add(wide ? value : (uint)value);
        }
        return values.ToArray();
    }

    private static string Reference(ulong bits, bool wide, bool signed, int width, bool upper)
    {
        string format = (upper ? "X" : "x") + width;
        if (wide) return signed ? unchecked((long)bits).ToString(format, CultureInfo.InvariantCulture) : bits.ToString(format, CultureInfo.InvariantCulture);
        return signed ? unchecked((int)bits).ToString(format, CultureInfo.InvariantCulture) : ((uint)bits).ToString(format, CultureInfo.InvariantCulture);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SignedUnsignedPaddingCaseAndEveryShortCapacityMatchDotNet(int optimization)
    {
        var code = new StringBuilder("using Standard; using Standard::Text; int main() {\n");
        int[] widths = [0, 1, 4, 8, 16, 24];
        foreach (bool wide in new[] { false, true })
        foreach (bool signed in new[] { false, true })
        {
            ulong[] values = Values(wide);
            string integer = wide ? "ulong" : "uint", suffix = wide ? "ul" : "u";
            string type = signed ? wide ? "long" : "int" : integer;
            string[] expected = values.SelectMany(value => widths.SelectMany(width => new[] { false, true }.Select(upper => Reference(value, wide, signed, width, upper)))).ToArray();
            code.AppendLine($$"""
                {
                    {{integer}}[{{values.Length}}] values = [{{string.Join(",", values.Select(value => value + suffix))}}];
                    nuint[{{widths.Length}}] widths = [{{string.Join(",", widths.Select(value => "(nuint)" + value))}}];
                    nuint[{{expected.Length}}] lengths = [{{string.Join(",", expected.Select(value => "(nuint)" + value.Length))}}];
                    readonly(char)* expected = "{{string.Concat(expected)}}";
                    char[26] output;
                    nuint offset = 0; nuint record = 0;
                    for (nuint index = 0; index < {{values.Length}}; index++) {
                        for (nuint width = 0; width < {{widths.Length}}; width++) {
                            for (int style = 0; style < 2; style++) {
                                bool uppercase = style == 1;
                                nuint length = lengths[record];
                                for (nuint i = 0; i < 26; i++) output[i] = '!';
                                nuint written = 999;
                                if (!IntegerFormatting::TryFormatHex(({{type}})values[index], &output[1], length, ref written, widths[width], uppercase) || written != length) return 1;
                                for (nuint i = 0; i < length; i++) if (output[i + 1] != expected[offset + i]) return 2;
                                for (nuint i = length + 1; i < 26; i++) if (output[i] != '!') return 3;
                                if (output[0] != '!') return 4;
                                for (nuint capacity = 0; capacity < length; capacity++) {
                                    written = 999;
                                    if (IntegerFormatting::TryFormatHex(({{type}})values[index], &output[1], capacity, ref written, widths[width], uppercase) || written != 0) return 5;
                                    for (nuint i = 0; i < length; i++) if (output[i + 1] != expected[offset + i]) return 6;
                                    for (nuint i = length + 1; i < 26; i++) if (output[i] != '!') return 7;
                                    if (output[0] != '!') return 8;
                                }
                                offset += length; record++;
                            }
                        }
                    }
                }
                """);
        }
        code.AppendLine("return 0; }");
        Run(code.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryIntegralTypeCanBePassedDirectlyWithItsOwnSignedWidth(int optimization)
    {
        Run("""
            using Standard; using Standard::Text;
            int main() {
                char[32] output; nuint written = 0;
                byte unsignedByte = (byte)255; sbyte signedByte = (sbyte)-1;
                ushort unsignedShort = (ushort)65535; short signedShort = (short)-1;
                if (!IntegerFormatting::TryFormatHex(unsignedByte, output, 32, ref written) || written != 2 || output[0] != 'F' || output[1] != 'F') return 1;
                if (!IntegerFormatting::TryFormatHex(signedByte, output, 32, ref written) || written != 2 || output[0] != 'F' || output[1] != 'F') return 2;
                if (!IntegerFormatting::TryFormatHex(unsignedShort, output, 32, ref written) || written != 4) return 3;
                if (!IntegerFormatting::TryFormatHex(signedShort, output, 32, ref written) || written != 4) return 4;
                for (nuint i = 0; i < 4; i++) if (output[i] != 'F') return 5;
                signedByte = (sbyte)-128; signedShort = (short)-32768;
                if (!IntegerFormatting::TryFormatHex(signedByte, output, 32, ref written) || written != 2 || output[0] != '8' || output[1] != '0') return 6;
                if (!IntegerFormatting::TryFormatHex(signedShort, output, 32, ref written) || written != 4 || output[0] != '8') return 7;
                for (nuint i = 1; i < 4; i++) if (output[i] != '0') return 8;
                nuint unsignedPointer = (nuint)0 - 1;
                nint signedPointer = (nint)-1;
                nuint pointerDigits = (nuint)sizeof(nuint) * 2;
                if (!IntegerFormatting::TryFormatHex(unsignedPointer, output, 32, ref written) || written != pointerDigits) return 9;
                for (nuint i = 0; i < written; i++) if (output[i] != 'F') return 10;
                if (!IntegerFormatting::TryFormatHex(signedPointer, output, 32, ref written) || written != pointerDigits) return 11;
                for (nuint i = 0; i < written; i++) if (output[i] != 'F') return 12;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DefaultsZeroHugePaddingAndZeroCapacityRespectPreflight(int optimization)
    {
        Run("""
            using Standard; using Standard::Text;
            int main() {
                char[26] output;
                for (nuint i = 0; i < 26; i++) output[i] = '!';
                nuint written = 999;
                if (!IntegerFormatting::TryFormatHex(255u, &output[1], 24, ref written) || written != 2 || output[1] != 'F' || output[2] != 'F' || output[3] != '!') return 1;
                if (!IntegerFormatting::TryFormatHex(255u, &output[1], 24, ref written, 8) || written != 8) return 2;
                for (nuint i = 1; i < 7; i++) if (output[i] != '0') return 3;
                if (output[7] != 'F' || output[8] != 'F' || output[9] != '!') return 4;
                if (!IntegerFormatting::TryFormatHex(0ul, &output[1], 24, ref written, 0, false) || written != 1 || output[1] != '0') return 5;
                for (nuint i = 0; i < 26; i++) output[i] = '!';
                nuint huge = (nuint)0 - 1;
                written = 999;
                if (IntegerFormatting::TryFormatHex(-1, &output[1], 24, ref written, huge) || written != 0) return 6;
                if (IntegerFormatting::TryFormatHex(0ul, &output[1], 24, ref written, huge - 1, false) || written != 0) return 7;
                for (nuint i = 0; i < 26; i++) if (output[i] != '!') return 8;
                char*? absent = null;
                if (IntegerFormatting::TryFormatHex(0u, absent!, 0, ref written, 0) || written != 0) return 9;
                if (IntegerFormatting::TryFormatHex(0ul, absent!, 0, ref written, huge) || written != 0) return 10;
                return 0;
            }
            """, optimization);
    }

    private static void Run(string program, int optimization)
    {
        SourceFile[] sources = [
            new("IntegerFormatting.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard/core/Text/IntegerFormatting.gf"))),
            new("main.gf", program)
        ];
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true });
        foreach (string forbidden in new[] { "@__gflat_eh_", "personality", "gflat$Memory$", "@malloc", "@free" }) Assert.DoesNotContain(forbidden, ir);
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
