using System.Globalization;
using System.Numerics;
using System.Text;

namespace gflat.Tests.Std.Numerics;

public sealed class RationalIntegralTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExactIntegralBoundsMatchBigIntegerOraclesAndEveryAllocationFailurePreservesOwners(int optimization)
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
            int Check(readonly(char)* input, readonly(char)* truncate, readonly(char)* floor, readonly(char)* ceiling)
            {
                Rational value = Rational::Parse(input);
                String original = value.ToString();
                BigInteger expected = BigInteger::Parse(truncate);
                BigInteger actual = value.Truncate();
                if (!actual.Equals(&expected)) return 1;
                expected = BigInteger::Parse(floor);
                actual = value.Floor();
                if (!actual.Equals(&expected)) return 2;
                expected = BigInteger::Parse(ceiling);
                actual = value.Ceiling();
                if (!actual.Equals(&expected)) return 3;
                String after = value.ToString();
                if (!original.Equals(&after)) return 4;
                return 0;
            }
            BigInteger Apply(immutable(Rational)* value, int mode)
            {
                if (mode == 0) return value.Truncate();
                if (mode == 1) return value.Floor();
                return value.Ceiling();
            }
            int main()
            {
            """);
        var cases = new List<(BigInteger N, BigInteger D)>();
        foreach (long n in new long[] { long.MinValue, -7, -6, -1, 0, 1, 6, 7, long.MaxValue })
        foreach (long d in new long[] { long.MinValue, -3, -1, 1, 3, long.MaxValue }) cases.Add((n, d));
        var random = new Random(318745);
        for (int i = 0; i < 64; i++)
        {
            byte[] top = new byte[1 + i * 2]; byte[] bottom = new byte[1 + i];
            random.NextBytes(top); random.NextBytes(bottom);
            BigInteger denominator = new(bottom);
            if (denominator.IsZero) denominator = 1;
            cases.Add((new BigInteger(top), denominator));
        }
        for (int bit = 0; bit <= 1024; bit += 64)
        {
            BigInteger whole = BigInteger.One << bit;
            cases.Add((whole * 3 - 1, 3)); cases.Add((-whole * 3 + 1, 3));
        }
        foreach (var (n, d) in cases)
        {
            BigInteger quotient = BigInteger.DivRem(n, d, out BigInteger remainder);
            bool positive = n.Sign == d.Sign;
            BigInteger floor = quotient - (!remainder.IsZero && !positive ? 1 : 0);
            BigInteger ceiling = quotient + (!remainder.IsZero && positive ? 1 : 0);
            string Decimal(BigInteger value) => value.ToString(CultureInfo.InvariantCulture);
            program.AppendLine($"if (Check(\"{Decimal(n)}/{Decimal(d)}\", \"{Decimal(quotient)}\", \"{Decimal(floor)}\", \"{Decimal(ceiling)}\") != 0) return 10;");
            program.AppendLine("if (live != 0) return 11;");
        }
        program.AppendLine("""
                {
                    Rational value = Rational::Parse("-10000000000000000000000000000000000000001/3");
                    String before = value.ToString();
                    for (int mode = 0; mode < 3; mode++)
                    {
                        int allocations = 0;
                        {
                            int first = attempts;
                            BigInteger result = Apply(&value, mode);
                            allocations = attempts - first;
                        }
                        for (int failure = 0; failure < allocations; failure++)
                        {
                            BigInteger output = new(99l);
                            int owners = live;
                            failAfter = failure;
                            bool caught = false;
                            try { output = Apply(&value, mode); }
                            catch (Exception* error) { caught = true; }
                            failAfter = -1;
                            if (!caught || output.ToLong() != 99l || live != owners) return 12;
                            String after = value.ToString();
                            if (!before.Equals(&after)) return 13;
                        }
                    }
                }
                return live;
            }
            """);
        var result = CompilerTestHelper.Run(StandardTestSources.Numerics.Append(new SourceFile("rational-integral.gf", program.ToString())), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("RATIONAL_INTEGRAL.md", 0);
}
