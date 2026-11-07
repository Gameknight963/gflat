using System.Globalization;
using System.Numerics;
using System.Text;

namespace gflat.Tests.Std.Numerics;

public sealed class RationalTryFormatTests
{
    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("RATIONAL_TRY_FORMAT.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CanonicalFractionsFormatAtomicallyAtEveryCapacityWithoutAllocation(int optimization)
    {
        var inputs = new List<(BigInteger N, BigInteger D)> { (0, 1), (0, -99), (1, 1), (-1, 1), (6, 8),
            (-6, 8), (6, -8), (-6, -8), (long.MinValue, -1), (long.MinValue, long.MaxValue),
            (BigInteger.Pow(10, 300) + 1, BigInteger.Pow(10, 301) + 3),
            (-(BigInteger.Pow(10, 300) + 1), BigInteger.Pow(10, 301) + 3) };
        var random = new Random(791352);
        for (int i = 0; i < 24; i++)
        {
            byte[] n = new byte[24], d = new byte[24];
            random.NextBytes(n); random.NextBytes(d);
            BigInteger denominator = new(d);
            if (denominator.IsZero) denominator = 1;
            inputs.Add((new BigInteger(n), denominator));
        }
        var program = new StringBuilder("""
            using Standard;
            using Standard::Memory;
            using Standard::Numerics;

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
            int Check(readonly(char)* input, readonly(char)* expected, nuint count)
            {
                Rational value = Rational::Parse(input);
                Rational saved = value;
                nuint hash = value.GetHashCode();
                int before = attempts;
                int baseline = live;
                deny = true;
                nuint written = 123;
                Span<char> empty = new();
                if (value.TryFormat(empty, ref written) || written != 0) return 1;
                char* invalid = ((nuint)1).reinterpret<char*>();
                if (value.TryFormat(invalid, 0, ref written) || written != 0) return 2;
                char[1024] buffer;
                for (nuint capacity = 0; capacity <= count + 2; capacity++)
                for (int form = 0; form < 2; form++)
                {
                    for (nuint i = 0; i < 1024; i++) buffer[i] = '#';
                    written = 123;
                    bool success = false;
                    if (form == 0) success = value.TryFormat(&buffer[1], capacity, ref written);
                    else success = value.TryFormat(new Span<char>(&buffer[1], capacity), ref written);
                    if (success != (capacity >= count)) return 3;
                    nuint expectedWritten = 0;
                    if (success) expectedWritten = count;
                    if (written != expectedWritten) return 4;
                    for (nuint i = 0; i < 1024; i++)
                    {
                        char wanted = '#';
                        if (success && i > 0 && i <= count) wanted = expected[i - 1];
                        if (buffer[i] != wanted) return 5;
                    }
                }
                if (attempts != before || live != baseline || value.GetHashCode() != hash) return 6;
                deny = false;
                if (!value.Equals(&saved)) return 7;
                return 0;
            }
            int main()
            {
                int result = 0;
            """);
        foreach (var (numerator, denominator) in inputs)
        {
            BigInteger divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
            BigInteger n = numerator / divisor, d = denominator / divisor;
            if (d.Sign < 0) { n = -n; d = -d; }
            string expected = Text(n) + (d.IsOne ? "" : "/" + Text(d));
            string input = Text(numerator) + "/" + Text(denominator);
            program.AppendLine($"result = Check(\"{input}\", \"{expected}\", {expected.Length}); if (result != 0) return result;");
        }
        program.AppendLine("return live; }");
        var sources = StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Numerics/BigInteger.gf", "core/Numerics/Rational.gf"))
            .Append(new SourceFile("rational-try-format.gf", program.ToString()));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Rational formatting exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    private static string Text(BigInteger value) => value.ToString(CultureInfo.InvariantCulture);
}
