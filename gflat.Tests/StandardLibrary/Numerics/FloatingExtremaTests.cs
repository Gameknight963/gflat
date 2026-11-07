namespace gflat.Tests.Std.Numerics;

public sealed class FloatingExtremaTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("FLOATING_EXTREMA.md", 0);

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void MagnitudesAndClampMatchDotNetIncludingSignedZeros(bool wide, int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_extrema_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (var (a, b, c) in Inputs(wide))
            {
                ulong minimum;
                ulong maximum;
                ulong clamp = 0;
                bool rejected = false;
                if (wide)
                {
                    double x = BitConverter.UInt64BitsToDouble(a), y = BitConverter.UInt64BitsToDouble(b), z = BitConverter.UInt64BitsToDouble(c);
                    minimum = BitConverter.DoubleToUInt64Bits(Math.MinMagnitude(x, y));
                    maximum = BitConverter.DoubleToUInt64Bits(Math.MaxMagnitude(x, y));
                    try { clamp = BitConverter.DoubleToUInt64Bits(Math.Clamp(x, y, z)); }
                    catch (ArgumentException) { rejected = true; }
                }
                else
                {
                    float x = BitConverter.UInt32BitsToSingle((uint)a), y = BitConverter.UInt32BitsToSingle((uint)b), z = BitConverter.UInt32BitsToSingle((uint)c);
                    minimum = BitConverter.SingleToUInt32Bits(MathF.MinMagnitude(x, y));
                    maximum = BitConverter.SingleToUInt32Bits(MathF.MaxMagnitude(x, y));
                    try { clamp = BitConverter.SingleToUInt32Bits(Math.Clamp(x, y, z)); }
                    catch (ArgumentException) { rejected = true; }
                }
                foreach (ulong value in new[] { a, b, c, minimum, maximum, clamp, rejected ? 1ul : 0ul }) writer.Write(value);
            }
            string scalar = wide ? "double" : "float";
            string integer = wide ? "ulong" : "uint";
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            string program = $$"""
                using Standard;
                using Standard::Numerics;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                extern "C" int ferror(void* file);
                bool Same({{scalar}} value, ulong expected) nothrow
                {
                    {{integer}} bits = ({{integer}})expected;
                    {{scalar}} target = bits.reinterpret<{{scalar}}>();
                    if (Math::IsNaN(target)) return Math::IsNaN(value);
                    return value.reinterpret<{{integer}}>() == bits;
                }
                {{scalar}} Minimum({{scalar}} a, {{scalar}} b) nothrow => Math::MinMagnitude(a, b);
                {{scalar}} Maximum({{scalar}} a, {{scalar}} b) nothrow => Math::MaxMagnitude(a, b);
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    ulong[7] row = default(ulong[7]);
                    while (true)
                    {
                        nuint bytes = fread(&row[0], 1, 56, file!);
                        if (bytes == 0) break;
                        if (bytes != 56) return 2;
                        {{integer}} aBits = ({{integer}})row[0];
                        {{integer}} bBits = ({{integer}})row[1];
                        {{integer}} cBits = ({{integer}})row[2];
                        {{scalar}} a = aBits.reinterpret<{{scalar}}>();
                        {{scalar}} b = bBits.reinterpret<{{scalar}}>();
                        {{scalar}} c = cBits.reinterpret<{{scalar}}>();
                        if (!Same(Minimum(a, b), row[3]) || !Same(Maximum(a, b), row[4])) return 3;
                        {{scalar}} result = a;
                        bool caught = false;
                        try { result = Math::Clamp(a, b, c); }
                        catch (Exception* error) { caught = true; }
                        if (row[6] == 1ul)
                        {
                            if (!caught || !Same(result, row[0])) return 4;
                        }
                        else if (caught || !Same(result, row[5])) return 5;
                    }
                    if (ferror(file!) != 0) return 6;
                    return 0;
                }
                """;
            var result = CompilerTestHelper.Run(StandardTestSources.Load("core/Numerics/Math.gf")
                .Append(new SourceFile("floating-extrema.gf", program)), optimization);
            Assert.True(result.ExitCode == 0, $"Floating extrema exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }

    private static IEnumerable<(ulong, ulong, ulong)> Inputs(bool wide)
    {
        int fraction = wide ? 52 : 23;
        ulong sign = 1ul << (wide ? 63 : 31);
        ulong infinity = ((1ul << (wide ? 11 : 8)) - 1) << fraction;
        ulong one = wide ? 0x3ff0000000000000ul : 0x3f800000ul;
        ulong[] positive = [0, 1, 2, (1ul << fraction) - 1, 1ul << fraction, one - 1, one, one + 1, infinity - 1, infinity, infinity + 1, infinity + (1ul << (fraction - 1)), infinity + (1ul << fraction) - 1];
        ulong[] values = positive.Concat(positive.Select(x => x | sign)).ToArray();
        foreach (ulong a in values)
        foreach (ulong b in values)
        foreach (ulong c in new[] { 0ul, sign, one, one | sign, infinity, infinity | sign, infinity + 1 })
            yield return (a, b, c);
        var random = new Random(817293);
        byte[] bytes = new byte[8];
        ulong Next()
        {
            random.NextBytes(bytes);
            ulong value = BitConverter.ToUInt64(bytes);
            return wide ? value : (uint)value;
        }
        for (int i = 0; i < 12000; i++) yield return (Next(), Next(), Next());
    }
}
