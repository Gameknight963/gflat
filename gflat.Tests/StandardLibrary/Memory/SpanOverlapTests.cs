namespace gflat.Tests.Std.Memory;

public sealed class SpanOverlapTests
{
    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("SPAN_OVERLAP.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EverySmallSubrangeMatchesIndependentIntervalOracleWithoutReadingOrAllocating(int optimization)
    {
        var sources = StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Memory/span_overlap.gf"))
            .Append(new SourceFile("span-overlap.gf", """
                using Standard;
                using Standard::Memory;

                int attempts = 0;
                namespace Memory
                {
                    public replace void*? Allocate(nuint bytes) { attempts++; return null; }
                    public replace void Free(void* storage) nothrow { }
                }
                int main() nothrow
                {
                    int[32] data;
                    for (nuint first = 0; first <= 16; first++)
                    for (nuint firstLength = 0; firstLength <= 16; firstLength++)
                    for (nuint second = 0; second <= 16; second++)
                    for (nuint secondLength = 0; secondLength <= 16; secondLength++)
                    {
                        Span<int> left = new(&data[first], firstLength);
                        Span<int> right = new(&data[second], secondLength);
                        bool expected = firstLength != 0 && secondLength != 0 &&
                            first < second + secondLength && second < first + firstLength;
                        if (left.Overlaps(right) != expected || right.Overlaps(left) != expected) return 1;
                        if (Standard::Memory::Overlaps(left, right) != expected) return 2;
                        Span<readonly(int)> readonlyLeft = left.AsReadOnly();
                        Span<readonly(int)> readonlyRight = right.AsReadOnly();
                        if (readonlyLeft.Overlaps(readonlyRight) != expected) return 3;
                    }
                    Span<int> empty = new();
                    Span<int> nonempty = new(((nuint)1).reinterpret<int*>(), 1);
                    if (empty.Overlaps(nonempty) || nonempty.Overlaps(empty) || empty.Overlaps(empty)) return 4;
                    // Unequal byte offsets can still overlap the same element's bytes.
                    Span<int> unaligned = new(((nuint)2).reinterpret<int*>(), 1);
                    if (!nonempty.Overlaps(unaligned) || !unaligned.Overlaps(nonempty)) return 5;
                    nuint maximum = (nuint)0 - (nuint)1;
                    Span<byte> high = new((maximum - 3).reinterpret<byte*>(), 3);
                    Span<byte> adjacent = new(maximum.reinterpret<byte*>(), 1);
                    if (high.Overlaps(adjacent) || adjacent.Overlaps(high)) return 6;
                    Span<byte> touching = new((maximum - 1).reinterpret<byte*>(), 1);
                    if (!high.Overlaps(touching) || !touching.Overlaps(high)) return 7;
                    return attempts;
                }
                """));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }
}
