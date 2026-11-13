namespace gflat.Tests.Std.Numerics;

public sealed class MathAngleTests
{
    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("MATH_ANGLES.md", 0);

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void ConstantsAndAngleConversionsMatchDotNetAcrossFloatingRepresentations(bool wide, int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_math_angles_" + Guid.NewGuid().ToString("N"));
        try
        {
            int fraction = wide ? 52 : 23, exponentCount = wide ? 2048 : 256;
            ulong sign = 1ul << (wide ? 63 : 31), mask = (1ul << fraction) - 1;
            var inputs = new HashSet<ulong>();
            for (int exponent = 0; exponent < exponentCount; exponent++)
            foreach (ulong mantissa in new[] { 0ul, 1ul, mask })
            {
                ulong bits = ((ulong)exponent << fraction) | mantissa;
                inputs.Add(bits); inputs.Add(bits | sign);
            }
            foreach (double value in new[] { -720d, -360d, -180d, -90d, -45d, -1d, 0d, 1d, 45d, 90d, 180d, 360d, 720d, Math.PI, Math.E, Math.Tau })
                inputs.Add(wide ? BitConverter.DoubleToUInt64Bits(value) : BitConverter.SingleToUInt32Bits((float)value));
            var random = new Random(712985);
            byte[] randomBytes = new byte[8];
            for (int i = 0; i < 1024; i++)
            {
                random.NextBytes(randomBytes);
                inputs.Add(wide ? BitConverter.ToUInt64(randomBytes) : BitConverter.ToUInt32(randomBytes));
            }
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (ulong bits in inputs)
            {
                if (wide)
                {
                    double value = BitConverter.UInt64BitsToDouble(bits);
                    writer.Write(bits);
                    writer.Write(BitConverter.DoubleToUInt64Bits(double.DegreesToRadians(value)));
                    writer.Write(BitConverter.DoubleToUInt64Bits(double.RadiansToDegrees(value)));
                }
                else
                {
                    float value = BitConverter.UInt32BitsToSingle((uint)bits);
                    writer.Write((uint)bits);
                    writer.Write(BitConverter.SingleToUInt32Bits(float.DegreesToRadians(value)));
                    writer.Write(BitConverter.SingleToUInt32Bits(float.RadiansToDegrees(value)));
                }
            }
            string scalar = wide ? "double" : "float", integer = wide ? "ulong" : "uint";
            string literal = "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            string code = $$"""
                using Standard::Numerics;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode) nothrow;
                extern "C" nuint fread(void* data, nuint size, nuint count, void* stream) nothrow;
                extern "C" int ferror(void* stream) nothrow;
                extern "C" int fclose(void* stream) nothrow;
                int main()
                {
                    if ((Math::PI).reinterpret<ref immutable(ulong)>() != {{BitConverter.DoubleToUInt64Bits(Math.PI)}}ul) return 1;
                    if ((Math::E).reinterpret<ref immutable(ulong)>() != {{BitConverter.DoubleToUInt64Bits(Math.E)}}ul) return 2;
                    if ((Math::Tau).reinterpret<ref immutable(ulong)>() != {{BitConverter.DoubleToUInt64Bits(Math.Tau)}}ul) return 3;
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 4;
                    defer { fclose(file!); }
                    {{integer}}[3] row;
                    {{scalar}}[2] actual;
                    while (true)
                    {
                        nuint read = fread(&row[0], 1, (nuint)sizeof({{integer}}) * 3, file!);
                        if (read == 0) break;
                        if (read != (nuint)sizeof({{integer}}) * 3) return 5;
                        {{scalar}} value = (row[0]).reinterpret<ref immutable({{scalar}})>();
                        actual[0] = Math::DegreesToRadians(value);
                        actual[1] = Math::RadiansToDegrees(value);
                        for (nuint i = 0; i < 2; i++)
                        {
                            {{scalar}} expected = (row[i + 1]).reinterpret<ref immutable({{scalar}})>();
                            if (Math::IsNaN(expected))
                            {
                                if (!Math::IsNaN(actual[i])) return 6;
                            }
                            else if ((actual[i]).reinterpret<ref immutable({{integer}})>() != row[i + 1]) return (int)i + 7;
                        }
                    }
                    return ferror(file!);
                }
                """;
            var sources = StandardTestSources.Load("core/Numerics/Math.gf")
                .Append(new SourceFile("math-angles.gf", code)).ToArray();
            string ir = Compiler.Emit(sources, options: new() { InternalizeFunctions = true });
            Assert.DoesNotContain("@malloc", ir);
            Assert.DoesNotContain("@__gflat_eh_", ir);
            Assert.DoesNotContain(" fast ", ir);
            var result = CompilerTestHelper.Run(sources, optimization);
            Assert.True(result.ExitCode == 0, $"Math angle exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }
}
