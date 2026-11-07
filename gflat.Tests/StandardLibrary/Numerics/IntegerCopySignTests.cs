using System.Globalization;
using System.Numerics;
using System.Text;

namespace gflat.Tests.Std.Numerics;

public sealed class IntegerCopySignTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SignedWidthsMatchDotNetIncludingMinimumValueOverflow(int optimization)
    {
        var program = new StringBuilder("""
            using Standard;
            using Standard::Numerics;

            """);
        foreach (var (type, width) in new[] { ("sbyte", 8), ("short", 16), ("int", 32),
            ("long", 64), ("nint", IntPtr.Size * 8), ("extralong", 128) })
        {
            program.AppendLine($"int Check_{type}({type} value, {type} sign, {type} expected, bool overflow)");
            program.AppendLine("""
                {
                    bool caught = false;
                """);
            program.AppendLine($"    {type} output = ({type})42;");
            program.AppendLine("    try { output = Math::CopySign(value, sign); }");
            program.AppendLine("    catch (Exception* error) { caught = true; }");
            program.AppendLine("    if (caught != overflow) return 1;");
            program.AppendLine("    if (overflow) { if (output != 42) return 2; }");
            program.AppendLine("    else if (output != expected) return 3;");
            program.AppendLine("    return 0; }");
        }
        program.AppendLine("int main() {");
        var random = new Random(317904);
        foreach (var (type, width) in new[] { ("sbyte", 8), ("short", 16), ("int", 32),
            ("long", 64), ("nint", IntPtr.Size * 8), ("extralong", 128) })
        {
            BigInteger minimum = -(BigInteger.One << (width - 1));
            BigInteger maximum = -minimum - 1;
            var values = new List<BigInteger> { minimum, minimum + 1, -127, -1, 0, 1, 127, maximum };
            for (int bit = 0; bit < width - 1; bit++)
            {
                values.Add(BigInteger.One << bit);
                values.Add(-(BigInteger.One << bit));
            }
            for (int i = 0; i < 32; i++)
            {
                byte[] bytes = new byte[width / 8];
                random.NextBytes(bytes);
                values.Add(new BigInteger(bytes));
            }
            foreach (BigInteger value in values.Distinct())
            foreach (BigInteger sign in new[] { minimum, -BigInteger.One, BigInteger.Zero, BigInteger.One, maximum })
            {
                bool overflow = false;
                BigInteger expected = 0;
                try { expected = (BigInteger)Int128.CopySign((Int128)value, (Int128)sign); }
                catch (OverflowException) { overflow = true; }
                if (expected < minimum || expected > maximum) overflow = true;
                if (overflow) expected = 0;
                string Literal(BigInteger number)
                {
                    BigInteger bits = number & ((BigInteger.One << 128) - 1);
                    string high = (bits >> 64).ToString("x", CultureInfo.InvariantCulture);
                    string low = (bits & ulong.MaxValue).ToString("x", CultureInfo.InvariantCulture);
                    return $"(({type})(((extralong)0x{high}ul << 64) | (extralong)0x{low}ul))";
                }
                program.AppendLine($"if (Check_{type}({Literal(value)}, {Literal(sign)}, {Literal(expected)}, {overflow.ToString().ToLowerInvariant()}) != 0) return {width};");
            }
        }
        program.AppendLine("return 0; }");
        var sources = StandardTestSources.Load("core/Numerics/Math.gf")
            .Append(new SourceFile("integer-copy-sign.gf", program.ToString()));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("INTEGER_COPY_SIGN.md", 0);
}
