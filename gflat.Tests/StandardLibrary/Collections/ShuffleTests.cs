namespace gflat.Tests.Std.Collections;

public sealed class ShuffleTests
{
    private static IEnumerable<SourceFile> Sources(string program) => new[] {
        "Collections/IEnumerable.gf", "Queries/Enumerable.gf", "Collections/span_shuffle.gf",
        "Numerics/PseudoRandom.gf", "Memory/Span.gf", "Collections/Vector.gf"
    }.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard/core", name))))
        .Append(new SourceFile("main.gf", program));

    private static ulong Next(ref ulong state)
    {
        state = unchecked(state + 0x9e3779b97f4a7c15UL);
        ulong word = state;
        word = unchecked((word ^ (word >> 30)) * 0xbf58476d1ce4e5b9UL);
        word = unchecked((word ^ (word >> 27)) * 0x94d049bb133111ebUL);
        return word ^ (word >> 31);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PermutationsAndGeneratorStateMatchIndependentFisherYatesReference(int optimization)
    {
        ulong state = 0xfedcba9876543210UL;
        var checks = new System.Text.StringBuilder();
        foreach (int size in new[] { 0, 1, 2, 3, 7, 16, 31, 64, 129 })
        {
            var expected = Enumerable.Range(0, size).ToArray();
            for (int remaining = size; remaining > 1; remaining--)
            {
                ulong bound = (ulong)remaining, threshold = unchecked(0UL - bound) % bound;
                ulong word;
                do word = Next(ref state); while (word < threshold);
                int selected = (int)(word % bound);
                (expected[remaining - 1], expected[selected]) = (expected[selected], expected[remaining - 1]);
            }
            checks.AppendLine($$"""
                {
                    int[{{size + 2}}] buffer;
                    buffer[0] = -111; buffer[{{size + 1}}] = -222;
                    for (int i = 0; i < {{size}}; i++) buffer[i + 1] = i;
                    Span<int> view = new(&buffer[1], {{size}});
                    Shuffle<int>(view, ref random);
                    if (buffer[0] != -111 || buffer[{{size + 1}}] != -222) return 1;
                    {{string.Join("\n", expected.Select((value, index) => $"if (buffer[{index + 1}] != {value}) return 2;"))}}
                }
                """);
        }
        ulong next = Next(ref state);
        var result = CompilerTestHelper.Run(Sources($$"""
            using Standard; using Standard::Memory; using Standard::Numerics; using Standard::Queries; using Standard::Collections;
            int main() {
                PseudoRandom random = new(0xfedcba9876543210ul);
                {{checks}}
                if (random.NextULong() != {{next}}ul) return 3;
                return 0;
            }
            """), optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ShufflingNoncopyableValuesPreservesEveryOwnerAndDoesNotAllocate(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard; using Standard::Memory; using Standard::Numerics; using Standard::Queries; using Standard::Collections;
            extern void* malloc(nuint size);
            extern void free(void* storage);
            int allocations = 0;
            replace void*? Memory::Allocate(nuint bytes) { allocations++; return malloc(bytes); }
            replace void Memory::Free(void* storage) nothrow { free(storage); }
            struct Item {
                private int* destroyed;
                public int Key;
                public Item(int* counts, int key) { destroyed = counts; Key = key; }
                ~Item() { destroyed[Key]++; }
            }
            int main() {
                int[65] destroyed;
                for (int i = 0; i < 65; i++) destroyed[i] = 0;
                PseudoRandom random = new(42ul);
                {
                    Vector<Item> values = new();
                    for (int i = 0; i < 65; i++) values.Add(new Item(destroyed, i));
                    int before = allocations;
                    for (int iteration = 0; iteration < 100; iteration++) {
                        Shuffle<Item>(values.AsSpan(), ref random);
                        bool[65] seen;
                        for (int i = 0; i < 65; i++) seen[i] = false;
                        for (nuint i = 0; i < values.Count; i++) {
                            int key = values[i].Key;
                            if (key < 0 || key >= 65 || seen[key]) return 1;
                            seen[key] = true;
                        }
                    }
                    if (allocations != before) return 2;
                    for (int i = 0; i < 65; i++) if (destroyed[i] != 0) return 3;
                }
                for (int i = 0; i < 65; i++) if (destroyed[i] != 1) return 4;
                return 0;
            }
            """), optimization);
        Assert.Equal(0, result.ExitCode);
    }
}
