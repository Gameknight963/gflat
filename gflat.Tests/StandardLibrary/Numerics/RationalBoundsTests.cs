using System.Globalization;
using System.Numerics;
using System.Text;

namespace gflat.Tests.Std.Numerics;

public sealed class RationalBoundsTests
{
    private readonly record struct Fraction(BigInteger N, BigInteger D)
    {
        public string Text
        {
            get
            {
                BigInteger gcd = BigInteger.GreatestCommonDivisor(N, D);
                BigInteger n = N / gcd, d = D / gcd;
                string result = n.ToString(CultureInfo.InvariantCulture);
                if (!d.IsOne) result += "/" + d.ToString(CultureInfo.InvariantCulture);
                return result;
            }
        }
        public int Compare(Fraction other) => (N * other.D).CompareTo(other.N * D);
    }

    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("RATIONAL_BOUNDS.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BoundsMatchExactCrossProductOraclesAndFailuresPreserveInputsAndDestination(int optimization)
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
            int Check(readonly(char)* a, readonly(char)* b, readonly(char)* x,
                readonly(char)* low, readonly(char)* high, readonly(char)* clamped)
            {
                Rational first = Rational::Parse(a);
                Rational second = Rational::Parse(b);
                Rational value = Rational::Parse(x);
                Rational lower = Rational::Min(&first, &second);
                Rational upper = Rational::Max(&first, &second);
                Rational output = Rational::Clamp(&value, &lower, &upper);
                String text = lower.ToString(); if (!text.Equals(low)) return 1;
                text = upper.ToString(); if (!text.Equals(high)) return 2;
                text = output.ToString(); if (!text.Equals(clamped)) return 3;
                first = new(99l); second = new(99l); value = new(99l);
                text = output.ToString(); if (!text.Equals(clamped)) return 4;
                return 0;
            }
            Rational Apply(immutable(Rational)* first, immutable(Rational)* second, immutable(Rational)* value, int mode)
            {
                if (mode == 0) return Rational::Min(first, second);
                if (mode == 1) return Rational::Max(first, second);
                return Rational::Clamp(value, first, second);
            }
            int main()
            {
            """);
        var values = new List<Fraction> { new(0, 1), new(1, 1), new(-1, 1), new(-7, 3), new(7, 3),
            new(6, 3), new(long.MinValue, 1), new(long.MaxValue, 1), new(1, BigInteger.One << 256) };
        var random = new Random(271945);
        for (int i = 0; i < 48; i++)
        {
            byte[] n = new byte[i + 1], d = new byte[i + 2]; random.NextBytes(n); random.NextBytes(d);
            values.Add(new(new BigInteger(n), BigInteger.Abs(new BigInteger(d)) + 1));
        }
        for (int i = 0; i < 160; i++)
        {
            Fraction a = values[random.Next(values.Count)], b = values[random.Next(values.Count)];
            if (i % 7 == 0) b = a;
            Fraction value = values[random.Next(values.Count)];
            Fraction lower = a.Compare(b) <= 0 ? a : b;
            Fraction upper = a.Compare(b) >= 0 ? a : b;
            Fraction clamped = value.Compare(lower) < 0 ? lower : value.Compare(upper) > 0 ? upper : value;
            program.AppendLine($"if (Check(\"{a.Text}\", \"{b.Text}\", \"{value.Text}\", \"{lower.Text}\", \"{upper.Text}\", \"{clamped.Text}\") != 0) return 10;");
            program.AppendLine("if (live != 0) return 11;");
        }
        program.AppendLine("""
                {
                    Rational first = Rational::Parse("-10000000000000000000000000000000000000001/17");
                    Rational second = Rational::Parse("10000000000000000000000000000000000000001/3");
                    Rational value = Rational::Parse("10000000000000000000000000000000000000001/7");
                    String a = first.ToString(); String b = second.ToString(); String x = value.ToString();
                    for (int mode = 0; mode < 3; mode++)
                    {
                        int allocations = 0;
                        {
                            int before = attempts;
                            Rational result = Apply(&first, &second, &value, mode);
                            allocations = attempts - before;
                        }
                        for (int failure = 0; failure < allocations; failure++)
                        {
                            Rational output = new(99l);
                            int owners = live;
                            failAfter = failure;
                            bool caught = false;
                            try { output = Apply(&first, &second, &value, mode); }
                            catch (Exception* error) { caught = true; }
                            failAfter = -1;
                            if (!caught || live != owners) return 12;
                            String kept = output.ToString();
                            String aa = first.ToString(); String bb = second.ToString(); String xx = value.ToString();
                            if (!kept.Equals("99") || !aa.Equals(&a) || !bb.Equals(&b) || !xx.Equals(&x)) return 13;
                        }
                    }
                    Rational output = new(99l);
                    bool caught = false;
                    try { output = Rational::Clamp(&value, &second, &first); }
                    catch (Exception* error) { caught = true; }
                    String kept = output.ToString();
                    if (!caught || !kept.Equals("99")) return 14;
                }
                return live;
            }
            """);
        var result = CompilerTestHelper.Run(StandardTestSources.Numerics.Append(new SourceFile("rational-bounds.gf", program.ToString())), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
