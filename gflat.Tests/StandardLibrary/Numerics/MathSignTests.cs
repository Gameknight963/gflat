using System.Text;

namespace gflat.Tests.Std.Numerics;

public sealed class MathSignTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IntegerBoundariesAndIeeeBitPatternsMatchHostSigns(int optimization)
    {
        var program = new StringBuilder("""
            using Standard;
            using Standard::Numerics;

            int CheckFloat(uint bits, int expected)
            {
                bool caught = false;
                try
                {
                    int result = Math::Sign(bits.reinterpret<float>());
                    if (expected == 2 || result != expected) return 1;
                }
                catch (Exception* error) { caught = true; }
                if (caught != (expected == 2)) return 2;
                return 0;
            }
            int CheckDouble(ulong bits, int expected)
            {
                bool caught = false;
                try
                {
                    int result = Math::Sign(bits.reinterpret<double>());
                    if (expected == 2 || result != expected) return 3;
                }
                catch (Exception* error) { caught = true; }
                if (caught != (expected == 2)) return 4;
                return 0;
            }
            int main()
            {
                for (int value = -32768; value <= 32767; value++)
                {
                    int expected = 0;
                    if (value < 0) expected = -1;
                    if (value > 0) expected = 1;
                    if (Math::Sign((short)value) != expected) return 10;
                    if (value >= -128 && value <= 127 && Math::Sign((sbyte)value) != expected) return 11;
                }
            """);
        foreach ((string type, int width) in new[] { ("int", 32), ("long", 64), ("extralong", 128), ("nint", IntPtr.Size * 8) })
        {
            program.AppendLine($$"""
                {
                    {{type}} minimum = ({{type}})1 << {{width - 1}};
                    {{type}} maximum = ~minimum;
                    if (Math::Sign(minimum) != -1 || Math::Sign(maximum) != 1 || Math::Sign(({{type}})0) != 0) return 12;
                    if (Math::Sign(({{type}})-1) != -1 || Math::Sign(({{type}})1) != 1) return 13;
                }
                """);
        }
        foreach (string type in new[] { "byte", "ushort", "uint", "ulong", "nuint" })
            program.AppendLine($"if (Math::Sign(({type})0) != 0 || Math::Sign(({type})1) != 1 || Math::Sign(({type})(~({type})0)) != 1) return 14;");
        var random = new Random(428391);
        uint[] floats = [0, 0x80000000, 1, 0x80000001, 0x007fffff, 0x807fffff, 0x00800000, 0x80800000,
            0x7f7fffff, 0xff7fffff, 0x7f800000, 0xff800000, 0x7fc00000, 0xffc00000, 0x7f800001, 0xff800001];
        ulong[] doubles = [0, 0x8000000000000000, 1, 0x8000000000000001, 0x000fffffffffffff, 0x800fffffffffffff,
            0x0010000000000000, 0x8010000000000000, 0x7fefffffffffffff, 0xffefffffffffffff,
            0x7ff0000000000000, 0xfff0000000000000, 0x7ff8000000000000, 0xfff8000000000000,
            0x7ff0000000000001, 0xfff0000000000001];
        foreach (uint bits in floats.Concat(Enumerable.Range(0, 128).Select(_ => (uint)random.NextInt64(0, 1L << 32))))
        {
            float value = BitConverter.UInt32BitsToSingle(bits);
            int expected = float.IsNaN(value) ? 2 : Math.Sign(value);
            program.AppendLine($"if (CheckFloat(0x{bits:x8}u, {expected}) != 0) return 20;");
        }
        foreach (ulong bits in doubles.Concat(Enumerable.Range(0, 128).Select(_ => (ulong)random.NextInt64() | ((ulong)random.Next(2) << 63))))
        {
            double value = BitConverter.UInt64BitsToDouble(bits);
            int expected = double.IsNaN(value) ? 2 : Math.Sign(value);
            program.AppendLine($"if (CheckDouble(0x{bits:x16}ul, {expected}) != 0) return 21;");
        }
        program.AppendLine("return 0; }");
        var sources = StandardTestSources.Load("core/Numerics/Math.gf")
            .Append(new SourceFile("sign-tests.gf", program.ToString()));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Sign exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
