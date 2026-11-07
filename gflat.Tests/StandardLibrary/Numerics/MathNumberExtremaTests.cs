namespace gflat.Tests.Std.Numerics;

public sealed class MathNumberExtremaTests
{
    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("FLOATING_NUMBER_EXTREMA.md", 0);

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void ExtremaMatchDotNetForSpecialValuesAndSeededBitPatterns(bool wide, int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_number_extrema_" + Guid.NewGuid().ToString("N"));
        try
        {
            ulong sign = 1ul << (wide ? 63 : 31);
            ulong infinity = wide ? 0x7ff0000000000000ul : 0x7f800000ul;
            ulong one = wide ? 0x3ff0000000000000ul : 0x3f800000ul;
            ulong[] special = [0, sign, 1, sign | 1, one, one | sign, infinity - 1,
                sign | (infinity - 1), infinity, sign | infinity, infinity | 1,
                sign | infinity | 7, infinity | (1ul << (wide ? 51 : 22))];
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                void Write(ulong a, ulong b)
                {
                    if (wide)
                    {
                        double left = BitConverter.UInt64BitsToDouble(a), right = BitConverter.UInt64BitsToDouble(b);
                        writer.Write(a); writer.Write(b);
                        foreach (double value in new[] { double.MinNumber(left, right), double.MaxNumber(left, right),
                            double.MinMagnitudeNumber(left, right), double.MaxMagnitudeNumber(left, right) })
                            writer.Write(BitConverter.DoubleToUInt64Bits(value));
                    }
                    else
                    {
                        float left = BitConverter.UInt32BitsToSingle((uint)a), right = BitConverter.UInt32BitsToSingle((uint)b);
                        writer.Write((uint)a); writer.Write((uint)b);
                        foreach (float value in new[] { float.MinNumber(left, right), float.MaxNumber(left, right),
                            float.MinMagnitudeNumber(left, right), float.MaxMagnitudeNumber(left, right) })
                            writer.Write(BitConverter.SingleToUInt32Bits(value));
                    }
                }
                foreach (ulong a in special)
                foreach (ulong b in special) Write(a, b);
                var random = new Random(982145);
                byte[] bytes = new byte[16];
                for (int i = 0; i < 4096; i++)
                {
                    random.NextBytes(bytes);
                    ulong a = BitConverter.ToUInt64(bytes), b = BitConverter.ToUInt64(bytes, 8);
                    if (!wide) { a = (uint)a; b = (uint)b; }
                    Write(a, b);
                    Write(a, a ^ sign);
                    Write(a, infinity | 1);
                    Write(infinity | 1, b);
                }
            }
            string scalar = wide ? "double" : "float", integer = wide ? "ulong" : "uint";
            string literal = "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            string program = $$"""
                using Standard::Numerics;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode) nothrow;
                extern "C" nuint fread(void* data, nuint size, nuint count, void* stream) nothrow;
                extern "C" int fclose(void* stream) nothrow;
                extern "C" int ferror(void* stream) nothrow;
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    {{integer}}[6] row;
                    {{scalar}}[4] actual;
                    while (true)
                    {
                        nuint read = fread(&row[0], 1, (nuint)sizeof({{integer}}) * 6, file!);
                        if (read == 0) break;
                        if (read != (nuint)sizeof({{integer}}) * 6) return 2;
                        {{scalar}} a = (row[0]).reinterpret<{{scalar}}>();
                        {{scalar}} b = (row[1]).reinterpret<{{scalar}}>();
                        actual[0] = Math::MinNumber(a, b);
                        actual[1] = Math::MaxNumber(a, b);
                        actual[2] = Math::MinMagnitudeNumber(a, b);
                        actual[3] = Math::MaxMagnitudeNumber(a, b);
                        for (nuint i = 0; i < 4; i++)
                        {
                            {{scalar}} expected = (row[i + 2]).reinterpret<{{scalar}}>();
                            if (Math::IsNaN(expected))
                            {
                                if (!Math::IsNaN(actual[i])) return 3;
                            }
                            else if ((actual[i]).reinterpret<{{integer}}>() != row[i + 2]) return (int)i + 4;
                        }
                    }
                    return ferror(file!);
                }
                """;
            var sources = StandardTestSources.Load("core/Numerics/Math.gf")
                .Append(new SourceFile("number-extrema.gf", program));
            var result = CompilerTestHelper.Run(sources, optimization);
            Assert.True(result.ExitCode == 0, $"Number extrema exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }
}
