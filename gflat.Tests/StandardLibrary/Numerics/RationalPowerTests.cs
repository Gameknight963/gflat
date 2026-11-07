using System.Globalization;
using System.Numerics;
using System.Text;

namespace gflat.Tests.Std.Numerics;

public sealed class RationalPowerTests
{
    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("RATIONAL_POW.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SignedPowersMatchExactBigIntegerOraclesAndAllocationFailuresPreserveOwners(int optimization)
    {
        var program = new StringBuilder("""
            using Standard;
            using Standard::Numerics;
            using Standard::Text;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int live = 0;
            int attempts = 0;
            int failAfter = -1;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    attempts++;
                    if (failAfter == 0) { failAfter = -1; return null; }
                    if (failAfter > 0) failAfter--;
                    void*? result = malloc(bytes);
                    if (result != null) live++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { live--; free(storage); }
            }
            int Check(readonly(char)* text, int exponent, readonly(char)* expected, bool valid)
            {
                Rational source = Rational::Parse(text);
                String before = source.ToString();
                Rational output = new(99l);
                bool caught = false;
                try { output = source.Pow(exponent); }
                catch (Exception* error) { caught = true; }
                if (caught == valid) return 1;
                String actual = output.ToString();
                if (valid) { if (!actual.Equals(expected)) return 2; }
                else if (!actual.Equals("99")) return 3;
                String after = source.ToString();
                if (!before.Equals(&after)) return 4;
                return 0;
            }
            int main()
            {
            """);
        foreach (var (n, d) in new (BigInteger, BigInteger)[] { (0, 1), (1, 1), (-1, 1), (2, 3), (-2, 3),
            (2, -3), (-12, -18), (long.MinValue, 1), (1, long.MinValue),
            (BigInteger.Pow(10, 40) + 7, -17) })
        foreach (int exponent in new[] { -17, -4, -3, -1, 0, 1, 2, 3, 8, 17 })
        {
            bool valid = !n.IsZero || exponent >= 0;
            string expected = "";
            if (valid)
            {
                BigInteger top = BigInteger.Pow(n, Math.Abs(exponent));
                BigInteger bottom = BigInteger.Pow(d, Math.Abs(exponent));
                if (exponent < 0) (top, bottom) = (bottom, top);
                if (bottom.Sign < 0) { top = -top; bottom = -bottom; }
                BigInteger gcd = BigInteger.GreatestCommonDivisor(top, bottom);
                top /= gcd; bottom /= gcd;
                expected = top.ToString(CultureInfo.InvariantCulture);
                if (!bottom.IsOne) expected += "/" + bottom.ToString(CultureInfo.InvariantCulture);
            }
            program.AppendLine($"if (Check(\"{n.ToString(CultureInfo.InvariantCulture)}/{d.ToString(CultureInfo.InvariantCulture)}\", {exponent}, \"{expected}\", {valid.ToString().ToLowerInvariant()}) != 0) return 10;");
            program.AppendLine("if (live != 0) return 11;");
        }
        program.AppendLine("""
                if (Check("-1", -2147483647 - 1, "1", true) != 0) return 12;
                if (Check("-1", 2147483647, "-1", true) != 0) return 13;
                if (Check("1", -2147483647 - 1, "1", true) != 0) return 14;
                if (Check("0", -2147483647 - 1, "", false) != 0) return 15;
                {
                    Rational source = Rational::Parse("-123456789012345678901/17");
                    String before = source.ToString();
                    int[4] powers = [-7, 0, 1, 8];
                    for (nuint mode = 0; mode < 4; mode++)
                    {
                        int allocations = 0;
                        {
                            int first = attempts;
                            Rational result = source.Pow(powers[mode]);
                            allocations = attempts - first;
                        }
                        for (int failure = 0; failure < allocations; failure++)
                        {
                            Rational output = new(99l);
                            int owners = live;
                            failAfter = failure;
                            bool caught = false;
                            try { output = source.Pow(powers[mode]); }
                            catch (Exception* error) { caught = true; }
                            failAfter = -1;
                            if (!caught || live != owners) return 16;
                            String kept = output.ToString();
                            String after = source.ToString();
                            if (!kept.Equals("99") || !before.Equals(&after)) return 17;
                        }
                    }
                }
                return live;
            }
            """);
        var result = CompilerTestHelper.Run(StandardTestSources.Numerics.Append(new SourceFile("rational-pow.gf", program.ToString())), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
