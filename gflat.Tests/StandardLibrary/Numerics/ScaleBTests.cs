namespace gflat.Tests.Std.Numerics;

public sealed class ScaleBTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("MATH_SCALE_B.md", 0);

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void ScalingMatchesDotNetAcrossEveryExponentAndSubnormalRounding(bool wide, int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_scale_b_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (var (bits, exponent) in Inputs(wide))
            {
                ulong expected = wide
                    ? BitConverter.DoubleToUInt64Bits(Math.ScaleB(BitConverter.UInt64BitsToDouble(bits), exponent))
                    : BitConverter.SingleToUInt32Bits(MathF.ScaleB(BitConverter.UInt32BitsToSingle((uint)bits), exponent));
                writer.Write(bits); writer.Write((long)exponent); writer.Write(expected);
            }
            string scalar = wide ? "double" : "float";
            string integer = wide ? "ulong" : "uint";
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            var result = CompilerTestHelper.Run(StandardTestSources.Load("core/Numerics/Math.gf")
                .Append(new SourceFile("scale-b.gf", $$"""
                using Standard::Numerics;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                extern "C" int ferror(void* file);
                {{scalar}} Scale({{scalar}} value, int exponent) nothrow => Math::ScaleB(value, exponent);
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    ulong[3] row = default(ulong[3]);
                    while (true)
                    {
                        nuint received = fread(&row[0], 1, 24, file!);
                        if (received == 0) break;
                        if (received != 24) return 2;
                        {{integer}} bits = ({{integer}})row[0];
                        {{integer}} expectedBits = ({{integer}})row[2];
                        {{scalar}} expected = expectedBits.reinterpret<ref immutable({{scalar}})>();
                        {{scalar}} actual = Scale(bits.reinterpret<ref immutable({{scalar}})>(), (int)row[1]);
                        if (expected != expected) { if (actual.reinterpret<ref immutable({{integer}})>() != bits) return 3; }
                        else if (actual.reinterpret<ref immutable({{integer}})>() != expectedBits) return 4;
                    }
                    if (ferror(file!) != 0) return 5;
                    return 0;
                }
                """)), optimization);
            Assert.True(result.ExitCode == 0, $"ScaleB exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }

    private static IEnumerable<(ulong, int)> Inputs(bool wide)
    {
        int fractionBits = wide ? 52 : 23;
        int maximum = wide ? 2047 : 255;
        ulong sign = 1ul << (wide ? 63 : 31);
        ulong mask = (1ul << fractionBits) - 1;
        for (int exponent = 0; exponent <= maximum; exponent++)
        foreach (ulong fraction in new[] { 0ul, 1ul, 2ul, mask / 2, mask - 1, mask })
        foreach (ulong polarity in new[] { 0ul, sign })
        {
            ulong bits = ((ulong)exponent << fractionBits) | fraction | polarity;
            foreach (int shift in new[] { int.MinValue, int.MaxValue, -1, 0, 1, -exponent, 1 - exponent - fractionBits,
                -exponent - fractionBits, -exponent - fractionBits - 1, maximum - exponent - 1, maximum - exponent })
                yield return (bits, shift);
        }
        for (int bit = 0; bit < fractionBits; bit++)
        foreach (ulong polarity in new[] { 0ul, sign })
        foreach (ulong fraction in new[] { 1ul << bit, (1ul << bit) + 1 })
        foreach (int shift in new[] { -1, 0, 1, fractionBits - bit - 1, fractionBits - bit, fractionBits - bit + 1,
            (wide ? 1023 : 127) + fractionBits - bit - 1, (wide ? 1023 : 127) + fractionBits - bit })
            yield return (fraction | polarity, shift);
        var random = new Random(927184);
        byte[] bytes = new byte[8];
        for (int i = 0; i < 4096; i++)
        {
            random.NextBytes(bytes);
            ulong bits = BitConverter.ToUInt64(bytes);
            yield return (wide ? bits : (uint)bits, random.Next(-2200, 2201));
        }
    }
}
