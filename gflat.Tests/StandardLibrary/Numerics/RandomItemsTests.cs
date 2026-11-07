namespace gflat.Tests.Std.Numerics;

public sealed class RandomItemsTests
{
    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Numerics/PseudoRandom.gf", "core/Numerics/pseudo_random_items.gf"))
            .Append(new SourceFile("random-items.gf", program)), optimization);
        Assert.True(result.ExitCode == 0, $"Random items exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("RANDOM_ITEMS.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SamplingMatchesIndependentSplitMixReferenceAndAdvancesExactly(int optimization)
    {
        ulong state = 0x194872fful;
        ulong Next()
        {
            state = unchecked(state + 0x9e3779b97f4a7c15ul);
            ulong value = state;
            value = unchecked((value ^ (value >> 30)) * 0xbf58476d1ce4e5b9ul);
            value = unchecked((value ^ (value >> 27)) * 0x94d049bb133111ebul);
            return value ^ (value >> 31);
        }
        var checks = new System.Text.StringBuilder();
        foreach (int count in new[] { 1, 2, 3, 7, 16, 31 })
        foreach (int length in new[] { 0, 1, 9, 33 })
        {
            var expected = new List<int>();
            for (int i = 0; i < length; i++)
            {
                ulong bound = (ulong)count;
                ulong threshold = unchecked(0ul - bound) % bound;
                ulong word;
                do { word = Next(); } while (word < threshold);
                expected.Add((int)(word % bound));
            }
            ulong after = Next();
            string values = string.Join(',', expected.DefaultIfEmpty(-1));
            checks.AppendLine($$"""
                {
                    int[{{Math.Max(1, length)}}] expected = [{{values}}];
                    int[33] destination = default(int[33]);
                    Span<int> source = new(&choices[0], {{count}});
                    random.GetItems(source.AsImmutable(), new Span<int>(&destination[0], {{length}}));
                    for (nuint i = 0; i < {{length}}; i++) { if (destination[i] != expected[i]) return 1; }
                    if (random.NextULong() != {{after}}ul) return 2;
                    Vector<int> owned = twin.GetItems(source.AsImmutable(), (nuint){{length}});
                    if (owned.Count != {{length}}) return 3;
                    for (nuint i = 0; i < owned.Count; i++) { if (owned[i] != expected[i]) return 4; }
                    if (twin.NextULong() != {{after}}ul) return 5;
                }
                """);
        }
        Run("using Standard;\nusing Standard::Memory;\nusing Standard::Collections;\nusing Standard::Numerics;\n\nint main() { int[31] choices = default(int[31]); for (int i = 0; i < 31; i++) choices[(nuint)i] = i; PseudoRandom random = new(0x194872fful); PseudoRandom twin = new(0x194872fful);" + checks + "return 0; }", optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OverlapUsesForwardReadsAndEmptyInputsDoNotAdvanceTheGenerator(int optimization)
        => Run("""
            using Standard;
            using Standard::Memory;
            using Standard::Collections;
            using Standard::Numerics;

            int main()
            {
                int[8] actual = [1, 2, 3, 4, 5, 6, 7, 8];
                int[8] expected = [1, 2, 3, 4, 5, 6, 7, 8];
                PseudoRandom random = new(81ul);
                PseudoRandom reference = new(81ul);
                Span<int> data = new(&actual[0], 8);
                GetItems(ref random, data.Slice(0, 5).AsImmutable(), data.Slice(2, 6));
                for (nuint i = 0; i < 6; i++) expected[i + 2] = expected[(nuint)reference.NextULong(5ul)];
                for (nuint i = 0; i < 8; i++) { if (actual[i] != expected[i]) return 1; }
                if (random.NextULong() != reference.NextULong()) return 2;
                Span<int> empty = new();
                random.GetItems(data.AsImmutable(), empty);
                Vector<int> zero = random.GetItems(data.AsImmutable(), (nuint)0);
                if (zero.Count != 0 || random.NextULong() != reference.NextULong()) return 3;
                for (int form = 0; form < 2; form++)
                {
                    bool caught = false;
                    try
                    {
                        if (form == 0) random.GetItems(empty.AsImmutable(), empty);
                        else { Vector<int> invalid = random.GetItems(empty.AsImmutable(), (nuint)0); }
                    }
                    catch (Exception* error) { caught = true; }
                    if (!caught || random.NextULong() != reference.NextULong()) return 4;
                }
                return 0;
            }
            """, optimization);
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CopyAndAllocationFailuresPreserveDestinationsAndReleasePartialResults(int optimization)
        => Run("""
            using Standard;
            using Standard::Memory;
            using Standard::Collections;
            using Standard::Numerics;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            bool deny = false;
            int attempts = 0;
            int allocations = 0;
            int releases = 0;
            int live = 0;
            int copies = 0;
            int failCopy = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    attempts++;
                    if (deny) return null;
                    void*? result = malloc(bytes);
                    if (result != null) allocations++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { releases++; free(storage); }
            }
            struct Owner
            {
                public int Id;
                public Owner(int id) { Id = id; live++; }
                public copy Owner(immutable(Owner)* source)
                {
                    copies++;
                    if (copies == failCopy) throw new Exception("copy failed");
                    Id = source.Id;
                    live++;
                }
                ~Owner() { live--; }
            }
            int main()
            {
                {
                    Owner[2] choices = [new Owner(10), new Owner(20)];
                    Owner[3] destination = [new Owner(-1), new Owner(-2), new Owner(-3)];
                    Span<Owner> source = new(&choices[0], 2);
                    Span<Owner> output = new(&destination[0], 3);
                    PseudoRandom random = new(17ul);
                    PseudoRandom reference = new(17ul);
                    int first = choices[(nuint)reference.NextULong(2ul)].Id;
                    reference.NextULong(2ul);
                    copies = 0; failCopy = 2;
                    bool caught = false;
                    try { random.GetItems(source.AsImmutable(), output); }
                    catch (Exception* error) { caught = true; }
                    if (!caught || live != 5 || copies != 2 || destination[0].Id != first || destination[1].Id != -2 || destination[2].Id != -3) return 1;
                    if (random.NextULong() != reference.NextULong()) return 2;
                    Vector<Owner> retained = new();
                    retained.Add(new Owner(99));
                    copies = 0; failCopy = 2;
                    reference.NextULong(2ul); reference.NextULong(2ul);
                    caught = false;
                    try { retained = random.GetItems(source.AsImmutable(), (nuint)3); }
                    catch (Exception* error) { caught = true; }
                    if (!caught || live != 6 || retained.Count != 1 || retained[0].Id != 99 || copies != 2) return 3;
                    if (random.NextULong() != reference.NextULong()) return 4;
                    failCopy = 0; copies = 0; deny = true;
                    caught = false;
                    try { retained = random.GetItems(source.AsImmutable(), (nuint)3); }
                    catch (Exception* error) { caught = true; }
                    deny = false;
                    if (!caught || copies != 0 || live != 6 || retained[0].Id != 99 || random.NextULong() != reference.NextULong()) return 5;
                    int before = attempts;
                    random.GetItems(source.AsImmutable(), output);
                    if (attempts != before || copies != 3 || live != 6 || choices[0].Id != 10 || choices[1].Id != 20) return 6;
                }
                if (allocations != releases) return 7;
                return live;
            }
            """, optimization);
}
