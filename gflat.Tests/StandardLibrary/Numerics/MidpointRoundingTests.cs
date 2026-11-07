namespace gflat.Tests.Std.Numerics;

public sealed class MidpointRoundingTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("MIDPOINT_ROUNDING.md", 0);

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void ModesMatchDotNetAcrossExponentsAndMidpointNeighbors(bool wide, int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_round_modes_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            foreach (ulong bits in Inputs(wide))
            {
                writer.Write(bits);
                for (int mode = 0; mode < 5; mode++)
                {
                    ulong expected = wide
                        ? BitConverter.DoubleToUInt64Bits(Math.Round(BitConverter.UInt64BitsToDouble(bits), (MidpointRounding)mode))
                        : BitConverter.SingleToUInt32Bits(MathF.Round(BitConverter.UInt32BitsToSingle((uint)bits), (MidpointRounding)mode));
                    writer.Write(expected);
                }
            }
            string type = wide ? "double" : "float";
            string integer = wide ? "ulong" : "uint";
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            var result = CompilerTestHelper.Run(StandardTestSources.Load("core/Numerics/Math.gf")
                .Append(new SourceFile("rounding-modes.gf", $$"""
                using Standard::Numerics;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                extern "C" int ferror(void* file);
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    ulong[6] row = default(ulong[6]);
                    while (true)
                    {
                        nuint received = fread(&row[0], 1, 48, file!);
                        if (received == 0) break;
                        if (received != 48) return 2;
                        {{integer}} bits = ({{integer}})row[0];
                        {{type}} value = bits.reinterpret<{{type}}>();
                        for (int mode = 0; mode < 5; mode++)
                        {
                            {{type}} actual = Math::Round(value, (MidpointRounding)mode);
                            {{integer}} expectedBits = ({{integer}})row[(nuint)mode + 1];
                            {{type}} expected = expectedBits.reinterpret<{{type}}>();
                            if (expected != expected)
                            {
                                if (actual == actual) return 3;
                            }
                            else if (actual.reinterpret<{{integer}}>() != expectedBits) return 4;
                        }
                    }
                    if (ferror(file!) != 0) return 5;
                    int[4] invalid = [-1, 5, -2147483647 - 1, 2147483647];
                    {{integer}}[3] unusual = [({{integer}})0, ({{integer}}){{(wide ? "0x7ff0000000000000ul" : "0x7f800000u")}}, ({{integer}}){{(wide ? "0x7ff8000000000000ul" : "0x7fc00000u")}}];
                    for (nuint i = 0; i < 4; i++)
                    {
                        for (nuint j = 0; j < 3; j++)
                        {
                            {{type}} output = ({{type}})123;
                            bool caught = false;
                            try { output = Math::Round(unusual[j].reinterpret<{{type}}>(), (MidpointRounding)invalid[i]); }
                            catch (Exception* error) { caught = true; }
                            if (!caught || output != ({{type}})123) return 6;
                        }
                    }
                    return 0;
                }
                """)), optimization);
            Assert.True(result.ExitCode == 0, $"Rounding modes exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { File.Delete(path); }
    }

    private static IEnumerable<ulong> Inputs(bool wide)
    {
        int fractionBits = wide ? 52 : 23;
        ulong sign = 1ul << (wide ? 63 : 31);
        ulong mask = (1ul << fractionBits) - 1;
        foreach (ulong fraction in new[] { 0ul, 1ul, 1ul << (fractionBits - 1), mask - 1, mask })
        for (ulong exponent = 0; exponent < (wide ? 2048ul : 256ul); exponent++)
        {
            ulong bits = (exponent << fractionBits) | fraction;
            yield return bits; yield return bits | sign;
        }
        for (int i = -256; i <= 256; i++)
        {
            if (wide)
            {
                double middle = i + 0.5;
                yield return BitConverter.DoubleToUInt64Bits(middle);
                yield return BitConverter.DoubleToUInt64Bits(Math.BitIncrement(middle));
                yield return BitConverter.DoubleToUInt64Bits(Math.BitDecrement(middle));
            }
            else
            {
                float middle = i + 0.5f;
                yield return BitConverter.SingleToUInt32Bits(middle);
                yield return BitConverter.SingleToUInt32Bits(MathF.BitIncrement(middle));
                yield return BitConverter.SingleToUInt32Bits(MathF.BitDecrement(middle));
            }
        }
        var random = new Random(827194);
        byte[] bytes = new byte[8];
        for (int i = 0; i < 8192; i++)
        {
            random.NextBytes(bytes);
            ulong bits = BitConverter.ToUInt64(bytes);
            yield return wide ? bits : (uint)bits;
        }
    }
}
