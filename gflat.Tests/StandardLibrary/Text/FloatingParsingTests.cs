using System.Globalization;
using System.Text;

namespace gflat.Tests.Std.Text;

public sealed class FloatingParsingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FiniteRoundTripTextMatchesHostBitsWithoutLanguageRuntime(int optimization)
    {
        uint[] singles = [0u, 0x80000000u, 1u, 0x80000001u, 0x007fffffu, 0x00800000u,
            0x3f000000u, 0x3f800000u, 0x3f800001u, 0xbf800001u, 0x7f7fffffu, 0xff7fffffu];
        ulong[] doubles = [0ul, 0x8000000000000000ul, 1ul, 0x8000000000000001ul,
            0x000ffffffffffffful, 0x0010000000000000ul, 0x3fe0000000000000ul,
            0x3ff0000000000000ul, 0x3ff0000000000001ul, 0xbff0000000000001ul,
            0x7feffffffffffffful, 0xffeffffffffffffful];
        var random = new Random(316194);
        singles = singles.Concat(Enumerable.Range(0, 24).Select(_ => (uint)random.NextInt64(0, 0x100000000L))
            .Where(bits => (bits & 0x7f800000u) != 0x7f800000u)).ToArray();
        doubles = doubles.Concat(Enumerable.Range(0, 24).Select(_ =>
        {
            byte[] bytes = new byte[8]; random.NextBytes(bytes); return BitConverter.ToUInt64(bytes);
        }).Where(bits => (bits & 0x7ff0000000000000ul) != 0x7ff0000000000000ul)).ToArray();
        string[] singleTexts = singles.Select(bits => BitConverter.UInt32BitsToSingle(bits).ToString("R", CultureInfo.InvariantCulture)).ToArray();
        string[] doubleTexts = doubles.Select(bits => BitConverter.UInt64BitsToDouble(bits).ToString("R", CultureInfo.InvariantCulture)).ToArray();
        // Compact loop fixtures avoid a separate emitted function/check sequence per number.
        RunTry($$"""
            using Standard::Text;
            int main()
            {
                readonly(char)*[{{singles.Length}}] singles = [{{string.Join(",", singleTexts.Select(Literal))}}];
                uint[{{singles.Length}}] singleBits = [{{string.Join(",", singles.Select(bits => bits + "u"))}}];
                for (nuint index = 0; index < {{singles.Length}}; index++)
                {
                    float value = 7.0f;
                    if (!TryParseFloat(singles[index], &value) || value.reinterpret<uint>() != singleBits[index]) return 1;
                }
                readonly(char)*[{{doubles.Length}}] doubles = [{{string.Join(",", doubleTexts.Select(Literal))}}];
                ulong[{{doubles.Length}}] doubleBits = [{{string.Join(",", doubles.Select(bits => bits + "ul"))}}];
                for (nuint index = 0; index < {{doubles.Length}}; index++)
                {
                    double value = 7.0;
                    if (!TryParseDouble(doubles[index], &value) || value.reinterpret<ulong>() != doubleBits[index]) return 2;
                }
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyWhitespaceAndPartiallyConsumedTextFailWithPositiveZero(int optimization)
    {
        string[] invalid = ["", "+", "-", ".", "+.", "1e", "1e+", "1e-", "0x", "0x1p+", "1x", "1_0",
            "1.25tail", "nan(", "nan()tail", "infinite", "true", "++1", "--1", "1..0", "1,2"];
        invalid = invalid.Concat(new[] { ' ', '\t', '\r', '\n' }
            .SelectMany(character => new[] { character + "1.25", "1.25" + character })).ToArray();
        RunTry($$"""
            using Standard::Text;
            int main()
            {
                readonly(char)*[{{invalid.Length}}] cases = [{{string.Join(",", invalid.Select(Literal))}}];
                for (nuint index = 0; index < {{invalid.Length}}; index++)
                {
                    float single = -7.0f;
                    double wide = -7.0;
                    if (TryParseFloat(cases[index], &single) || single.reinterpret<uint>() != 0u) return 10;
                    if (TryParseDouble(cases[index], &wide) || wide.reinterpret<ulong>() != 0ul) return 11;
                }
                char[3] high = [(char)(byte)255, '1', '\0'];
                float single = -1.0f;
                double wide = -1.0;
                if (TryParseFloat(high, &single) || single.reinterpret<uint>() != 0u) return 12;
                if (TryParseDouble(high, &wide) || wide.reinterpret<ulong>() != 0ul) return 13;
                if (!TryParseFloat("1.5\0ignored", &single) || single != 1.5f) return 14;
                if (!TryParseDouble("1.5\0ignored", &wide) || wide != 1.5) return 15;
                for (int whitespace = 9; whitespace <= 13; whitespace++)
                {
                    char[3] leading = [(char)whitespace, '1', '\0'];
                    char[3] trailing = ['1', (char)whitespace, '\0'];
                    if (TryParseFloat(leading, &single) || single.reinterpret<uint>() != 0u) return 16;
                    if (TryParseDouble(leading, &wide) || wide.reinterpret<ulong>() != 0ul) return 17;
                    if (TryParseFloat(trailing, &single) || single.reinterpret<uint>() != 0u) return 18;
                    if (TryParseDouble(trailing, &wide) || wide.reinterpret<ulong>() != 0ul) return 19;
                }
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DirectSingleRoundingHexSpecialValuesAndNativeRangeOutcomesAreAccepted(int optimization)
        => RunTry("""
            using Standard::Text;
            int main()
            {
                float single = 0.0f;
                double wide = 0.0;
                if (!TryParseFloat("1.00000005960464477539062500000000000000000000000001", &single)
                    || single.reinterpret<uint>() != 0x3f800001u) return 20;
                if (!TryParseFloat("0x1.000002p0", &single) || single.reinterpret<uint>() != 0x3f800001u) return 21;
                if (!TryParseDouble("-0X1.8P+2", &wide) || wide != -6.0) return 22;
                if (!TryParseFloat("-0", &single) || single.reinterpret<uint>() != 0x80000000u) return 23;
                if (!TryParseDouble("-0.0e+3", &wide) || wide.reinterpret<ulong>() != 0x8000000000000000ul) return 24;
                if (!TryParseFloat("1e9999", &single) || single.reinterpret<uint>() != 0x7f800000u) return 25;
                if (!TryParseDouble("-1e9999", &wide) || wide.reinterpret<ulong>() != 0xfff0000000000000ul) return 26;
                if (!TryParseFloat("-1e-9999", &single) || single.reinterpret<uint>() != 0x80000000u) return 27;
                if (!TryParseDouble("-1e-9999", &wide) || wide.reinterpret<ulong>() != 0x8000000000000000ul) return 28;
                if (!TryParseFloat("INF", &single) || single.reinterpret<uint>() != 0x7f800000u) return 29;
                if (!TryParseDouble("-infinity", &wide) || wide.reinterpret<ulong>() != 0xfff0000000000000ul) return 30;
                if (!TryParseFloat("nAn", &single) || single == single) return 31;
                if (!TryParseDouble("NAN(42)", &wide) || wide == wide) return 32;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OverlappingOutputCommitsAfterSyntaxValidationAndRespectsGuards(int optimization)
        => RunTry("""
            using Standard::Text;
            int main()
            {
                ulong[8] storage;
                char* text = (&storage[0]).reinterpret<char*>();
                storage[7] = 0x123456789abcdef0ul;
                for (nuint index = 0; index < 5; index++) text[index] = "1.25"[index];
                float* single = (&storage[0]).reinterpret<float*>();
                if (!TryParseFloat(text, single) || (*single).reinterpret<uint>() != 0x3fa00000u) return 40;
                for (nuint index = 0; index < 6; index++) text[index] = "1.25x"[index];
                single = (&storage[0]).reinterpret<float*>() + 1;
                if (TryParseFloat(text, single) || (*single).reinterpret<uint>() != 0u) return 41;
                for (nuint index = 0; index < 5; index++) text[index] = "-2.5"[index];
                double* wide = (&storage[0]).reinterpret<double*>();
                if (!TryParseDouble(text, wide) || (*wide).reinterpret<ulong>() != 0xc004000000000000ul) return 42;
                for (nuint index = 0; index < 10; index++) text[index] = "12345678x"[index];
                wide = (&storage[1]).reinterpret<double*>();
                if (TryParseDouble(text, wide) || (*wide).reinterpret<ulong>() != 0ul) return 43;
                if (storage[7] != 0x123456789abcdef0ul) return 44;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ThrowingWrappersRejectSyntaxButRetainNativeRangeValues(int optimization)
    {
        var sources = new[] { "core/Exception.gf", "libc/Memory/allocation.gf", "libc/Exceptions/unhandled.gf",
            "libc/Text/floating_parsing.gf", "libc/Text/floating_parsing_throwing.gf" }
            .Select(Source).Append(new SourceFile("floating-parse-throwing.gf", """
                using Standard::Text;
                int main()
                {
                    int caught = 0;
                    try { ParseFloat("12x"); } catch (ref Exception error) { caught++; }
                    try { ParseDouble(" 12"); } catch (ref Exception error) { caught++; }
                    if (caught != 2) return 50;
                    if (ParseFloat("1e9999").reinterpret<uint>() != 0x7f800000u) return 51;
                    if (ParseDouble("-1e-9999").reinterpret<ulong>() != 0x8000000000000000ul) return 52;
                    if (ParseFloat("1.25") != 1.25f || ParseDouble("2.5") != 2.5) return 53;
                    return 0;
                }
                """));
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }

    private static SourceFile Source(string relative)
        => new(relative, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", relative)));

    private static void RunTry(string program, int optimization)
    {
        SourceFile[] sources = [Source("libc/Text/floating_parsing.gf"), new("floating-parse-try.gf", program)];
        string ir = Compiler.Emit(sources, options: new() { NoStdLib = true, NoBuiltins = true, InternalizeFunctions = true });
        Assert.Equal((false, false), NativeToolchain.RequiredNativeRuntimes(ir));
        Assert.DoesNotContain("__gflat_eh_", ir, StringComparison.Ordinal);
        Assert.DoesNotContain("__gflat_stdio_", ir, StringComparison.Ordinal);
        Assert.DoesNotContain("@malloc", ir, StringComparison.Ordinal);
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }

    private static string Literal(string text)
    {
        var result = new StringBuilder("\"");
        foreach (char value in text)
            result.Append(value switch
            {
                '\0' => "\\0", '\t' => "\\t", '\r' => "\\r", '\n' => "\\n",
                '\\' => "\\\\", '"' => "\\\"", _ => value.ToString()
            });
        return result.Append('"').ToString();
    }
}
