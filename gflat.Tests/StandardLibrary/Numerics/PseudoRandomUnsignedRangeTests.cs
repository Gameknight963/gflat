using System.Text;

namespace gflat.Tests.Std.Numerics;

public sealed class PseudoRandomUnsignedRangeTests
{
    private static ulong Next(ref ulong state)
    {
        unchecked
        {
            state += 0x9e3779b97f4a7c15;
            ulong value = state;
            value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9;
            value = (value ^ (value >> 27)) * 0x94d049bb133111eb;
            return value ^ (value >> 31);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UnsignedRangesMatchIndependentRejectionOracleAndInvalidBoundsPreserveState(int optimization)
    {
        var program = new StringBuilder("""
            using Standard;
            using Standard::Numerics;

            int attempts = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes) { attempts++; return null; }
                public replace void Free(void* storage) nothrow { }
            }
            int main()
            {
            """);
        foreach (bool wide in new[] { false, true })
        {
            ulong maximum = wide ? ulong.MaxValue : uint.MaxValue;
            var ranges = new List<(ulong Min, ulong Max)> { (0, 1), (0, maximum), (maximum - 1, maximum),
                (maximum / 2, maximum), (1, maximum), (maximum / 2 - 2, maximum / 2 + 3), (17, 24) };
            var random = new Random(wide ? 247815 : 815742);
            for (int i = 0; i < 32; i++)
            {
                ulong a = (ulong)random.NextInt64() & maximum;
                ulong b = (ulong)random.NextInt64() & maximum;
                if (a == b) b = a + 1;
                ranges.Add((Math.Min(a, b), Math.Max(a, b)));
            }
            string type = wide ? "ulong" : "uint";
            string method = wide ? "NextULong" : "NextUInt";
            foreach (ulong seed in new ulong[] { 0, 1, 42, ulong.MaxValue, 0x8000000000000000 })
            foreach (var (minimum, max) in ranges)
            {
                program.AppendLine($"{{ PseudoRandom generator = new({seed}ul);");
                ulong state = seed;
                ulong width = max - minimum;
                ulong threshold = unchecked(0ul - width) % width;
                for (int draw = 0; draw < 4; draw++)
                {
                    ulong value;
                    do { value = Next(ref state); } while (value < threshold);
                    ulong expected = minimum + value % width;
                    program.AppendLine($"if (generator.{method}(({type}){minimum}ul, ({type}){max}ul) != ({type}){expected}ul) return 1;");
                }
                program.AppendLine($"if (generator.NextULong() != {Next(ref state)}ul) return 2; }}");
            }
        }
        program.AppendLine("""
                if (attempts != 0) return 3;
                for (int mode = 0; mode < 4; mode++)
                {
                    PseudoRandom value = new(42ul);
                    PseudoRandom control = new(42ul);
                    bool caught = false;
                    try
                    {
                        if (mode == 0) value.NextUInt(7u, 7u);
                        else if (mode == 1) value.NextUInt(9u, 3u);
                        else if (mode == 2) value.NextULong(7ul, 7ul);
                        else value.NextULong(18446744073709551615ul, 0ul);
                    }
                    catch (Exception* error) { caught = true; }
                    if (!caught || value.NextULong() != control.NextULong()) return 4;
                }
                return 0;
            }
            """);
        var result = CompilerTestHelper.Run(StandardTestSources.Load("core/Numerics/PseudoRandom.gf")
            .Append(new SourceFile("unsigned-random-range.gf", program.ToString())), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("RANDOM_UNSIGNED_RANGES.md", 0);
}
