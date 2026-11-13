namespace gflat.Tests.Std.Numerics;

public sealed class MathDecimalRoundingTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("DECIMAL_ROUNDING.md", 0);

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void DecimalPlacesMatchDotNetForAllModesAndPreserveZeroSigns(bool wide, int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_decimal_rounding_" + Guid.NewGuid().ToString("N"));
        int maximumDigits = wide ? 15 : 6;
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (ulong bits in Inputs(wide))
            for (int digits = 0; digits <= maximumDigits; digits++)
            {
                writer.Write(bits);
                writer.Write((ulong)digits);
                for (int mode = 0; mode < 5; mode++)
                {
                    ulong expected = wide
                        ? BitConverter.DoubleToUInt64Bits(Math.Round(BitConverter.UInt64BitsToDouble(bits), digits, (MidpointRounding)mode))
                        : BitConverter.SingleToUInt32Bits(MathF.Round(BitConverter.UInt32BitsToSingle((uint)bits), digits, (MidpointRounding)mode));
                    writer.Write(expected);
                }
            }
            string type = wide ? "double" : "float";
            string integer = wide ? "ulong" : "uint";
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            var result = CompilerTestHelper.Run(StandardTestSources.Load("core/Numerics/Math.gf")
                .Append(new SourceFile("decimal-rounding.gf", $$"""
                using Standard::Numerics;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* data, nuint size, nuint count, void* file);
                extern "C" int ferror(void* file);
                extern "C" int fclose(void* file);
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    ulong[7] row = default(ulong[7]);
                    while (true)
                    {
                        nuint received = fread(&row[0], 1, 56, file!);
                        if (received == 0) break;
                        if (received != 56) return 2;
                        {{type}} value = (({{integer}})row[0]).reinterpret<ref immutable({{type}})>();
                        int digits = (int)row[1];
                        for (int mode = 0; mode < 5; mode++)
                        {
                            {{type}} actual = Math::Round(value, digits, (MidpointRounding)mode);
                            {{integer}} expectedBits = ({{integer}})row[(nuint)mode + 2];
                            {{type}} expected = expectedBits.reinterpret<ref immutable({{type}})>();
                            if (expected != expected)
                            {
                                if (actual == actual) return 3;
                            }
                            else if (actual.reinterpret<ref immutable({{integer}})>() != expectedBits) return 4;
                        }
                        {{type}} defaultResult = Math::Round(value, digits);
                        {{type}} expectedDefault = (({{integer}})row[2]).reinterpret<ref immutable({{type}})>();
                        if (expectedDefault == expectedDefault && defaultResult.reinterpret<ref immutable({{integer}})>() != ({{integer}})row[2]) return 5;
                    }
                    if (ferror(file!) != 0) return 6;
                    int[4] invalidDigits = [-1, {{maximumDigits + 1}}, -2147483647 - 1, 2147483647];
                    {{integer}}[3] unusual = [({{integer}})0, ({{integer}}){{(wide ? "0x7ff0000000000000ul" : "0x7f800000u")}}, ({{integer}}){{(wide ? "0x7ff8000000000000ul" : "0x7fc00000u")}}];
                    for (nuint i = 0; i < 4; i++)
                    for (nuint j = 0; j < 3; j++)
                    {
                        {{type}} output = ({{type}})123;
                        bool caught = false;
                        try { output = Math::Round(unusual[j].reinterpret<ref immutable({{type}})>(), invalidDigits[i]); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || output != ({{type}})123) return 7;
                    }
                    int[4] invalidModes = [-1, 5, -2147483647 - 1, 2147483647];
                    for (nuint i = 0; i < 4; i++)
                    for (nuint j = 0; j < 3; j++)
                    {
                        {{type}} output = ({{type}})123;
                        bool caught = false;
                        try { output = Math::Round(unusual[j].reinterpret<ref immutable({{type}})>(), {{maximumDigits}}, (MidpointRounding)invalidModes[i]); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || output != ({{type}})123) return 8;
                    }
                    return 0;
                }
                """)), optimization);
            Assert.True(result.ExitCode == 0, $"Decimal rounding exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }

    private static IEnumerable<ulong> Inputs(bool wide)
    {
        if (wide)
        {
            foreach (ulong bits in new[] { 0ul, 0x8000000000000000ul, 1ul, 0x8000000000000001ul,
                0x7ff0000000000000ul, 0xfff0000000000000ul, 0x7ff8000000000001ul, 0x7feffffffffffffful }) yield return bits;
        }
        else
        {
            foreach (ulong bits in new[] { 0ul, 0x80000000ul, 1ul, 0x80000001ul,
                0x7f800000ul, 0xff800000ul, 0x7fc00001ul, 0x7f7ffffful }) yield return bits;
        }
        foreach (double value in new[] { 0.005, 0.015, 0.125, 0.375, 1.005, 2.675, 2.5, 3.5, 99.995,
            12345.6789, 99999999.0, 100000000.0, 100000001.0, 9999999999999998.0, 1e16, 1e17 })
        foreach (double signed in new[] { value, -value })
        {
            if (wide)
            {
                yield return BitConverter.DoubleToUInt64Bits(signed);
                yield return BitConverter.DoubleToUInt64Bits(Math.BitIncrement(signed));
                yield return BitConverter.DoubleToUInt64Bits(Math.BitDecrement(signed));
            }
            else
            {
                yield return BitConverter.SingleToUInt32Bits((float)signed);
                yield return BitConverter.SingleToUInt32Bits(MathF.BitIncrement((float)signed));
                yield return BitConverter.SingleToUInt32Bits(MathF.BitDecrement((float)signed));
            }
        }
        var random = new Random(58317);
        byte[] storage = new byte[8];
        for (int i = 0; i < 512; i++)
        {
            random.NextBytes(storage);
            yield return wide ? BitConverter.ToUInt64(storage) : BitConverter.ToUInt32(storage);
        }
    }
}
