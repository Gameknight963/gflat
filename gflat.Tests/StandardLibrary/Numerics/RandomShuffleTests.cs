namespace gflat.Tests.Std.Numerics;

public sealed class RandomShuffleTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("RANDOM_SHUFFLE.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void GeneratorFormsPreservePermutationsStateAndNoncopyableOwners(int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Numerics/PseudoRandom.gf", "core/Numerics/pseudo_random_shuffle.gf", "core/Collections/span_shuffle.gf"))
            .Append(new SourceFile("random-shuffle.gf", """
            using Standard;
            using Standard::Collections;
            using Standard::Memory;
            using Standard::Numerics;

            extern "C" void free(void* storage);
            int allocations = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes) { allocations++; return null; }
                public replace void Free(void* storage) nothrow { free(storage); }
            }
            struct Item
            {
                public int Key;
                private int* destroyed;
                public Item(int key, int* counts) { Key = key; destroyed = counts; }
                ~Item() { destroyed[Key]++; }
            }
            int main()
            {
                for (nuint length = 0; length <= 64; length++)
                {
                    int[66] first;
                    int[66] second;
                    int[66] reference;
                    for (nuint i = 0; i < 66; i++)
                    {
                        first[i] = (int)i; second[i] = (int)i; reference[i] = (int)i;
                    }
                    PseudoRandom a = new(42ul + (ulong)length);
                    PseudoRandom b = new(42ul + (ulong)length);
                    PseudoRandom c = new(42ul + (ulong)length);
                    a.Shuffle<int>(new Span<int>(&first[1], length));
                    Standard::Numerics::Shuffle<int>(ref b, new Span<int>(&second[1], length));
                    Standard::Collections::Shuffle<int>(new Span<int>(&reference[1], length), ref c);
                    for (nuint i = 0; i < 66; i++)
                        if (first[i] != reference[i] || second[i] != reference[i]) return 1;
                    ulong next = c.NextULong();
                    if (a.NextULong() != next || b.NextULong() != next) return 2;
                }
                int[4] destroyed = [0, 0, 0, 0];
                {
                    Item[4] owners = [new Item(0, &destroyed[0]), new Item(1, &destroyed[0]),
                        new Item(2, &destroyed[0]), new Item(3, &destroyed[0])];
                    PseudoRandom random = new(99ul);
                    for (int iteration = 0; iteration < 30; iteration++)
                    {
                        random.Shuffle<Item>(new Span<Item>(&owners[0], 4));
                        Standard::Numerics::Shuffle<Item>(ref random, new Span<Item>(&owners[0], 4));
                        int seen = 0;
                        for (nuint i = 0; i < 4; i++)
                        {
                            if (owners[i].Key < 0 || owners[i].Key > 3 || destroyed[i] != 0) return 3;
                            seen = seen | (1 << owners[i].Key);
                        }
                        if (seen != 15) return 4;
                    }
                }
                for (nuint i = 0; i < 4; i++) if (destroyed[i] != 1) return 5;
                if (allocations != 0) return 6;
                return 0;
            }
            """)), optimization);
        Assert.True(result.ExitCode == 0, $"Generator shuffle exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
