using System.Numerics;

namespace gflat.Tests.Std.Numerics;

public sealed class IntegerLogarithmTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("INTEGER_LOGARITHMS.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UnsignedLogsMatchDotNetForEveryUShortAndWidePowerBoundary(int optimization)
    {
        var inputs = new SortedSet<ulong>(Enumerable.Range(0, 65536).Select(value => (ulong)value)) { ulong.MaxValue };
        for (int bit = 0; bit < 64; bit++)
        {
            ulong power = 1ul << bit;
            inputs.Add(power - 1); inputs.Add(power); inputs.Add(power + 1);
        }
        var random = new Random(529843);
        for (int i = 0; i < 512; i++) inputs.Add((ulong)random.NextInt64() | ((ulong)random.Next(2) << 63));
        WithOracle(writer =>
        {
            foreach (ulong value in inputs)
            {
                writer.Write(value);
                writer.Write(BitOperations.Log2((uint)value));
                writer.Write(BitOperations.Log2(value));
                writer.Write(BitOperations.Log2((nuint)value));
            }
        }, """
            int Check(ulong value, int small, int large, int native) nothrow
            {
                if (BitOperations::Log2((uint)value) != small) return 3;
                if (BitOperations::Log2(value) != large) return 4;
                if (BitOperations::Log2((nuint)value) != native) return 5;
                return 0;
            }
            """, """
            int small = 0;
            int large = 0;
            int native = 0;
            if (fread(&small, 1, 4, file!) != 4 || fread(&large, 1, 4, file!) != 4 || fread(&native, 1, 4, file!) != 4) return 2;
            int failure = Check(bits, small, large, native);
            if (failure != 0) return failure;
            """, ["core/Numerics/BitOperations.gf"], optimization);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void FloatingExponentsMatchDotNetForEveryExponentAndSubnormalBoundary(bool wide, int optimization)
    {
        string scalar = wide ? "double" : "float";
        string bitsExpression = wide ? "bits" : "((uint)bits)";
        WithOracle(writer =>
        {
            foreach (ulong bits in FloatingInputs(wide))
            {
                writer.Write(bits);
                writer.Write(wide ? Math.ILogB(BitConverter.UInt64BitsToDouble(bits))
                    : MathF.ILogB(BitConverter.UInt32BitsToSingle((uint)bits)));
            }
        }, $$"""
            int Exponent({{scalar}} value) nothrow => Math::ILogB(value);
            """, $$"""
            int expected = 0;
            if (fread(&expected, 1, 4, file!) != 4) return 2;
            if (Exponent(Standard::Binary::BitConverter::{{(wide ? "UInt64BitsToDouble" : "UInt32BitsToFloat")}}({{bitsExpression}})) != expected) return 3;
            """, ["core/Binary/BitConverter.gf","core/Numerics/Math.gf"], optimization);
    }

    private static IEnumerable<ulong> FloatingInputs(bool wide)
    {
        int fractionBits = wide ? 52 : 23;
        ulong sign = 1ul << (wide ? 63 : 31);
        ulong mask = (1ul << fractionBits) - 1;
        for (ulong exponent = 0; exponent < (wide ? 2048ul : 256ul); exponent++)
        foreach (ulong fraction in new[] { 0ul, 1ul, mask >> 1, mask })
        {
            ulong bits = (exponent << fractionBits) | fraction;
            yield return bits;
            yield return bits | sign;
        }
        for (int bit = 0; bit < fractionBits; bit++)
        foreach (ulong bits in new[] { (1ul << bit) - 1, 1ul << bit, (1ul << bit) + 1 })
        {
            yield return bits;
            yield return bits | sign;
        }
        var random = new Random(352874);
        for (int i = 0; i < 512; i++)
        {
            ulong bits = (ulong)random.NextInt64() | ((ulong)random.Next(2) << 63);
            yield return wide ? bits : (uint)bits;
        }
    }

    private static void WithOracle(Action<BinaryWriter> write, string declarations, string check, string[] files, int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_integer_log_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path))) write(writer);
            string literal = "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            string program = $$"""
                using Standard::Numerics;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int ferror(void* file);
                extern "C" int fclose(void* file);
                {{declarations}}
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    ulong bits = 0ul;
                    while (true)
                    {
                        nuint read = fread(&bits, 1, 8, file!);
                        if (read == 0) break;
                        if (read != 8) return 2;
                        {{check}}
                    }
                    if (ferror(file!) != 0) return 6;
                    return 0;
                }
                """;
            var sources = StandardTestSources.Load(files).Append(new SourceFile("integer-log-tests.gf", program));
            var result = CompilerTestHelper.Run(sources, optimization);
            Assert.True(result.ExitCode == 0, $"Integer logarithm exit {result.ExitCode}: {result.StandardError}");
        }
        finally { File.Delete(path); }
    }
}
