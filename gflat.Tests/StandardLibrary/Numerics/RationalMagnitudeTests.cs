namespace gflat.Tests.Std.Numerics;

public sealed class RationalMagnitudeTests
{
    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("RATIONAL_MAGNITUDE.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MagnitudeAndReciprocalPreserveCanonicalOwnersAtEveryAllocationFailure(int optimization)
    {
        var sources = StandardTestSources.Numerics.Append(new SourceFile("rational-magnitude.gf", """
            using Standard;
            using Standard::Numerics;
            using Standard::Text;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int attempts = 0;
            int live = 0;
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
            Rational Apply(immutable(Rational)* value, bool reciprocal)
            {
                if (reciprocal) return value.Reciprocal();
                return value.Abs();
            }
            int Check(readonly(char)* text, readonly(char)* magnitude, readonly(char)* inverse)
            {
                Rational value = Rational::Parse(text);
                Rational expected = Rational::Parse(magnitude);
                Rational actual = value.Abs();
                if (!actual.Equals(&expected)) return 1;
                expected = Rational::Parse(inverse);
                actual = value.Reciprocal();
                if (!actual.Equals(&expected)) return 2;
                Rational restored = actual.Reciprocal();
                if (!restored.Equals(&value)) return 3;
                String before = value.ToString();
                for (int mode = 0; mode < 2; mode++)
                {
                    int allocations = 0;
                    {
                        int first = attempts;
                        Rational result = Apply(&value, mode != 0);
                        allocations = attempts - first;
                    }
                    for (int failure = 0; failure < allocations; failure++)
                    {
                        Rational output = new(99l);
                        int owners = live;
                        failAfter = failure;
                        bool caught = false;
                        try { output = Apply(&value, mode != 0); }
                        catch (Exception* error) { caught = true; }
                        failAfter = -1;
                        if (!caught || live != owners) return 4;
                        String kept = output.ToString();
                        if (!kept.Equals("99")) return 5;
                        String after = value.ToString();
                        if (!after.Equals(&before)) return 6;
                    }
                }
                return 0;
            }
            int main()
            {
                if (Check("-7/3", "7/3", "-3/7") != 0) return 10;
                if (Check("7/-3", "7/3", "-3/7") != 0) return 11;
                if (Check("-7/-3", "7/3", "3/7") != 0) return 12;
                if (Check("-1", "1", "-1") != 0) return 13;
                if (Check("1", "1", "1") != 0) return 14;
                if (Check("-9223372036854775808", "9223372036854775808", "-1/9223372036854775808") != 0) return 15;
                if (Check("-10000000000000000000000000000000000000001/3",
                    "10000000000000000000000000000000000000001/3",
                    "-3/10000000000000000000000000000000000000001") != 0) return 16;
                if (Check("1/10000000000000000000000000000000000000001",
                    "1/10000000000000000000000000000000000000001",
                    "10000000000000000000000000000000000000001") != 0) return 17;
                {
                    Rational zero = new();
                    Rational absolute = zero.Abs();
                    if (!absolute.Equals(&zero)) return 18;
                    Rational target = new(99l);
                    bool caught = false;
                    try { target = zero.Reciprocal(); }
                    catch (Exception* error) { caught = true; }
                    String unchanged = target.ToString();
                    if (!caught || !unchanged.Equals("99")) return 19;
                }
                return live;
            }
            """));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
