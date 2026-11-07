using System.Globalization;
using System.Numerics;
using System.Text;

namespace gflat.Tests.Std.Numerics;

public sealed class RationalClassificationTests
{
    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("RATIONAL_CLASSIFICATION.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CanonicalValuePropertiesMatchExactOraclesWithoutAllocation(int optimization)
    {
        var cases = new List<(BigInteger N, BigInteger D)> { (0, 1), (0, -17), (1, 1), (-1, -1),
            (-1, 1), (1, -1), (6, 3), (7, 3), (-6, 3), (long.MinValue, -1), (long.MinValue, long.MinValue) };
        var random = new Random(492713);
        for (int i = 0; i < 64; i++)
        {
            byte[] top = new byte[i + 1]; byte[] bottom = new byte[i + 2];
            random.NextBytes(top); random.NextBytes(bottom);
            BigInteger n = new(top), d = new(bottom);
            if (d.IsZero) d = 1;
            cases.Add((n, d)); cases.Add((n * d, d)); cases.Add((d, d));
        }
        var program = new StringBuilder("""
            using Standard;
            using Standard::Numerics;
            using Standard::Text;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int attempts = 0;
            int live = 0;
            bool deny = false;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    attempts++;
                    if (deny) return null;
                    void*? result = malloc(bytes);
                    if (result != null) live++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { live--; free(storage); }
            }
            bool Check(immutable(Rational)* value, int sign, bool zero, bool one, bool integer) nothrow
                => value.Sign == sign && value.IsZero == zero && value.IsOne == one && value.IsInteger == integer;
            int main()
            {
            """);
        foreach (var (n, d) in cases)
        {
            string input = n.ToString(CultureInfo.InvariantCulture) + "/" + d.ToString(CultureInfo.InvariantCulture);
            int sign = n.Sign * d.Sign;
            string Bool(bool value) => value.ToString().ToLowerInvariant();
            program.AppendLine($$"""
                {
                    Rational value = Rational::Parse("{{input}}");
                    String before = value.ToString();
                    int first = attempts;
                    deny = true;
                    if (!Check(&value, {{sign}}, {{Bool(n.IsZero)}}, {{Bool(n == d)}}, {{Bool(n % d == 0)}})) return 1;
                    deny = false;
                    if (attempts != first) return 2;
                    String after = value.ToString();
                    if (!before.Equals(&after)) return 3;
                }
                if (live != 0) return 4;
                """);
        }
        program.AppendLine("return live; }");
        var result = CompilerTestHelper.Run(StandardTestSources.Numerics.Append(new SourceFile("rational-classification.gf", program.ToString())), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
