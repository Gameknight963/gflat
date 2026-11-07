using System.Globalization;
using System.Numerics;
using System.Text;

namespace gflat.Tests.Std.Numerics;

public sealed class BigIntegerSpanParsingTests
{
    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("BIG_INTEGER_SPAN_PARSING.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CountedSlicesMatchDotNetForDecimalInputsAndRejectNondecimalText(int optimization)
    {
        var cases = new List<(string Text, bool Valid)> { ("", false), ("+", false), ("-", false),
            (" 12", false), ("12 ", false), ("12x", false), ("1_2", false), ("1.0", false),
            ("12\0", false), ("0\0x", false), ("12\n", false), ("++1", false), ("+-1", false),
            ("0", true), ("-0", true), ("+00000", true), ("-0001", true), ("9223372036854775808", true) };
        var random = new Random(982415);
        for (int i = 0; i < 96; i++)
        {
            byte[] bytes = new byte[random.Next(1, 97)];
            random.NextBytes(bytes);
            BigInteger value = new(bytes);
            string text = value.ToString(CultureInfo.InvariantCulture);
            cases.Add((text, true));
            if (value.Sign >= 0) cases.Add(("+000" + text, true));
        }
        var program = new StringBuilder("""
            using Standard;
            using Standard::Memory;
            using Standard::Numerics;

            int Check(readonly(char)* input, nuint count, bool valid, readonly(char)* expectedText)
            {
                Span<readonly(char)> text = new(input + 1, count);
                BigInteger value = new(77l);
                bool success = BigInteger::TryParse(text, ref value);
                if (success != valid) return 1;
                BigInteger expected = BigInteger::Parse(expectedText);
                if (!value.Equals(&expected)) return 2;
                value = new BigInteger(99l);
                bool caught = false;
                try { value = BigInteger::Parse(text); } catch (Exception* error) { caught = true; }
                if (caught == valid) return 3;
                if (valid) { if (!value.Equals(&expected)) return 4; }
                else if (value.ToLong() != 99l) return 5;
                if (input[0] != '!' || input[count + 1] != '?') return 6;
                return 0;
            }
            int main()
            {
                int result = 0;
            """);
        foreach (var (text, valid) in cases)
        {
            string expected = valid ? BigInteger.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) : "0";
            string literal = ("!" + text + "?").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\0", "\\0").Replace("\n", "\\n");
            program.AppendLine($"result = Check(\"{literal}\", {text.Length}, {valid.ToString().ToLowerInvariant()}, \"{expected}\"); if (result != 0) return result;");
        }
        program.AppendLine("return 0; }");
        Run(program.ToString(), optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyViewsAndEveryObservedAllocationFailurePreserveParsingPolicy(int optimization)
        => Run("""
            using Standard;
            using Standard::Memory;
            using Standard::Numerics;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int attempts = 0;
            int live = 0;
            int failAt = 0;
            bool deny = false;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    attempts++;
                    if (deny || attempts == failAt) return null;
                    void*? result = malloc(bytes);
                    if (result != null) live++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { live--; free(storage); }
            }
            int main()
            {
                {
                    BigInteger target = new(77l);
                    Span<readonly(char)> empty = new();
                    int before = attempts;
                    deny = true;
                    if (BigInteger::TryParse(empty, ref target) || !target.IsZero || attempts != before) return 1;
                    Span<readonly(char)> inaccessible = new(((nuint)1).reinterpret<readonly(char)*>(), 0);
                    if (BigInteger::TryParse(inaccessible, ref target) || !target.IsZero || attempts != before) return 2;
                    deny = false;
                    readonly(char)* input = "-123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890";
                    Span<readonly(char)> text = new(input, 91);
                    BigInteger expected = BigInteger::Parse(input);
                    for (int method = 0; method < 2; method++)
                    {
                        attempts = 0;
                        if (method == 0) target = BigInteger::Parse(text);
                        else if (!BigInteger::TryParse(text, ref target)) return 3;
                        int observed = attempts;
                        if (observed == 0 || !target.Equals(&expected)) return 4;
                        for (int failure = 1; failure <= observed; failure++)
                        {
                            target = new BigInteger(77l);
                            int baseline = live;
                            attempts = 0; failAt = failure;
                            bool caught = false;
                            try
                            {
                                if (method == 0) target = BigInteger::Parse(text);
                                else BigInteger::TryParse(text, ref target);
                            }
                            catch (BadAllocException* error) { caught = true; }
                            failAt = 0;
                            if (!caught || live != baseline || target.ToLong() != 77l) return 5;
                        }
                    }
                }
                return live;
            }
            """, optimization);

    private static void Run(string program, int optimization)
    {
        var sources = StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Numerics/BigInteger.gf"))
            .Append(new SourceFile("big-integer-span-parsing.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Span parsing exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
