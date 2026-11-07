using System.Globalization;
using Integer = System.Numerics.BigInteger;

namespace gflat.Tests.Std.Numerics;

public sealed class BigIntegerBitMetadataTests
{
    private static void Run(string code, int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Numerics/BigInteger.gf"))
            .Append(new SourceFile("big-integer-bit-metadata.gf", code)), optimization);
        Assert.True(result.ExitCode == 0, $"Bit metadata exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("BIG_INTEGER_BIT_METADATA.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BinaryAndDecimalBoundariesMatchDotNetWithoutChangingReceiver(int optimization)
    {
        var values = new HashSet<Integer> { 0, 1, -1 };
        foreach (int exponent in Enumerable.Range(0, 257).Concat(new[] { 511, 512, 513, 1023, 1024, 2048, 4096 }))
        {
            Integer power = Integer.One << exponent;
            foreach (Integer value in new[] { power - 1, power, power + 1 })
            { values.Add(value); values.Add(-value); }
        }
        foreach (int digits in new[] { 9, 18, 27, 36, 90, 180, 450, 900 })
        {
            Integer power = Integer.Pow(10, digits);
            foreach (Integer value in new[] { power - 1, power, power + 1 })
            { values.Add(value); values.Add(-value); }
        }
        var random = new Random(774018);
        for (int i = 0; i < 128; i++)
        {
            byte[] bytes = new byte[random.Next(1, 257)];
            random.NextBytes(bytes);
            values.Add(new Integer(bytes));
        }
        Integer[] rows = values.ToArray();
        string data = string.Join('|', rows.Select(value => value.ToString(CultureInfo.InvariantCulture))) + "|";
        Run(Tracking + $$"""
            int main()
            {
                readonly(char)* data = "{{data}}";
                long[{{rows.Length}}] lengths = [{{string.Join(",", rows.Select(value => value.GetBitLength() + "l"))}}];
                int[{{rows.Length}}] powers = [{{string.Join(",", rows.Select(value => value.IsPowerOfTwo ? 1 : 0))}}];
                int[{{rows.Length}}] small = [{{string.Join(",", rows.Select(value => Integer.Abs(value) <= ulong.MaxValue ? 1 : 0))}}];
                int[{{rows.Length}}] positive = [{{string.Join(",", rows.Select(value => value.Sign > 0 ? 1 : 0))}}];
                nuint offset = 0;
                for (nuint row = 0; row < {{rows.Length}}; row++)
                {
                    nuint count = 0;
                    while (data[offset + count] != '|') count++;
                    BigInteger value = BigInteger::Parse(data + offset, count);
                    BigInteger saved = value;
                    immutable(BigInteger)* view = &value;
                    int baseline = live;
                    attempts = 0;
                    if (small[row] != 0) failAt = 1;
                    if (view.GetBitLength() != lengths[row]) return 1;
                    failAt = 0;
                    int expected = 1;
                    if (small[row] != 0) expected = 0;
                    if (attempts != expected || live != baseline) return 2;
                    attempts = 0;
                    if (small[row] != 0 || positive[row] == 0) failAt = 1;
                    if (view.IsPowerOfTwo != (powers[row] != 0)) return 3;
                    failAt = 0;
                    if (positive[row] == 0) expected = 0;
                    if (attempts != expected || live != baseline || !value.Equals(&saved)) return 4;
                    offset += count + 1;
                }
                return live;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ScratchAllocationFailureLeavesLargeSignedOwnersReusable(int optimization)
        => Run(Tracking + """
            int main()
            {
                {
                    BigInteger value = BigInteger::Parse("1267650600228229401496703205376");
                    BigInteger negative = BigInteger::Parse("-1267650600228229401496703205376");
                    BigInteger saved = value;
                    BigInteger savedNegative = negative;
                    int baseline = live;
                    for (int form = 0; form < 3; form++)
                    {
                        attempts = 0; failAt = 1;
                        bool caught = false;
                        try
                        {
                            if (form == 0) { long bits = value.GetBitLength(); }
                            else if (form == 1) { long bits = negative.GetBitLength(); }
                            else { bool power = value.IsPowerOfTwo; }
                        }
                        catch (BadAllocException* error) { caught = true; }
                        failAt = 0;
                        if (!caught || live != baseline || !value.Equals(&saved) || !negative.Equals(&savedNegative)) return 1;
                        if (value.GetBitLength() != 101l || negative.GetBitLength() != 100l || !value.IsPowerOfTwo || negative.IsPowerOfTwo) return 2;
                    }
                }
                return live;
            }
            """, optimization);

    private const string Tracking = """
        using Standard;
        using Standard::Numerics;
        using Standard::Memory;
        using Standard::Queries;
        using Standard::Text;

        extern "C" void*? malloc(nuint bytes);
        extern "C" void free(void* storage);
        int live = 0;
        int attempts = 0;
        int failAt = 0;
        void*? latest = null;
        namespace Memory
        {
            public replace void*? Allocate(nuint bytes)
            {
                attempts++;
                if (attempts == failAt) return null;
                void*? result = malloc(bytes);
                if (result != null) live++;
                latest = result;
                return result;
            }
            public replace void Free(void* storage) nothrow { live--; free(storage); }
        }
        """;

}
