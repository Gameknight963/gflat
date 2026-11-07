namespace gflat.Tests.Std.Numerics;

public sealed class FloatingClassificationTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("FLOATING_CLASSIFICATION.md", 0);

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void EveryExponentAndMantissaBoundaryMatchesDotNet(bool wide, int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat_classification_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "oracle.bin");
            using (var writer = new BinaryWriter(File.Create(path)))
                foreach (ulong bits in Inputs(wide))
                {
                    writer.Write(bits);
                    writer.Write(Expected(bits, wide));
                }
            string scalar = wide ? "double" : "float";
            string bitsExpression = wide ? "bits" : "((uint)bits)";
            string literal = "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            string program = $$"""
                using Standard;
                using Standard::Numerics;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int ferror(void* file);
                extern "C" int fclose(void* file);

                int Classify({{scalar}} value) nothrow
                {
                    int result = 0;
                    if (Math::IsNaN(value)) result = result | 1;
                    if (Math::IsFinite(value)) result = result | 2;
                    if (Math::IsInfinity(value)) result = result | 4;
                    if (Math::IsPositiveInfinity(value)) result = result | 8;
                    if (Math::IsNegativeInfinity(value)) result = result | 16;
                    if (Math::IsNegative(value)) result = result | 32;
                    if (Math::IsPositive(value)) result = result | 64;
                    if (Math::IsNormal(value)) result = result | 128;
                    if (Math::IsSubnormal(value)) result = result | 256;
                    return result;
                }
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    ulong bits = 0ul;
                    int expected = 0;
                    while (true)
                    {
                        nuint read = fread(&bits, 1, 8, file!);
                        if (read == 0) break;
                        if (read != 8 || fread(&expected, 1, 4, file!) != 4) return 2;
                        {{scalar}} value = {{bitsExpression}}.reinterpret<{{scalar}}>();
                        if (Classify(value) != expected) return 3;
                    }
                    if (ferror(file!) != 0) return 4;
                    return 0;
                }
                """;
            var sources = StandardTestSources.Load("core/Numerics/Math.gf")
                .Append(new SourceFile("classification-tests.gf", program));
            var result = CompilerTestHelper.Run(sources, optimization);
            Assert.True(result.ExitCode == 0, $"Classification exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static IEnumerable<ulong> Inputs(bool wide)
    {
        int fractionBits = wide ? 52 : 23;
        ulong sign = 1ul << (wide ? 63 : 31);
        ulong fractionMask = (1ul << fractionBits) - 1;
        ulong[] fractions = [0, 1, 2, 1ul << (fractionBits - 1), fractionMask - 1, fractionMask];
        for (ulong exponent = 0; exponent < (wide ? 2048ul : 256ul); exponent++)
        foreach (ulong fraction in fractions)
        {
            ulong bits = (exponent << fractionBits) | fraction;
            yield return bits;
            yield return bits | sign;
        }
        var random = new Random(192893);
        for (int index = 0; index < 512; index++)
        {
            ulong bits = (ulong)random.NextInt64() | ((ulong)random.Next(2) << 63);
            yield return wide ? bits : (uint)bits;
        }
    }

    private static int Expected(ulong bits, bool wide)
    {
        double value = wide ? BitConverter.UInt64BitsToDouble(bits) : BitConverter.UInt32BitsToSingle((uint)bits);
        bool normal = wide ? double.IsNormal(value) : float.IsNormal((float)value);
        bool subnormal = wide ? double.IsSubnormal(value) : float.IsSubnormal((float)value);
        bool negative = wide ? double.IsNegative(value) : float.IsNegative((float)value);
        bool positive = wide ? double.IsPositive(value) : float.IsPositive((float)value);
        return (double.IsNaN(value) ? 1 : 0) | (double.IsFinite(value) ? 2 : 0)
            | (double.IsInfinity(value) ? 4 : 0) | (double.IsPositiveInfinity(value) ? 8 : 0)
            | (double.IsNegativeInfinity(value) ? 16 : 0) | (negative ? 32 : 0)
            | (positive ? 64 : 0) | (normal ? 128 : 0) | (subnormal ? 256 : 0);
    }
}
