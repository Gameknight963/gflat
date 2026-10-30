namespace gflat.Tests.Std.Collections;

public sealed class PartitionPointTests
{
    private const string Allocator = """
        using Standard; using Standard::Text; using Standard::Memory; using Standard::Collections;
        extern void*? malloc(nuint bytes); extern void free(void* storage);
        int attempts = 0; int allocated = 0; int released = 0; bool deny = false;
        namespace Memory {
            public replace void*? Allocate(nuint bytes) {
                attempts++; if (deny) return null;
                void*? result = malloc(bytes); if (result != null) allocated++; return result;
            }
            public replace void Free(void* storage) nothrow { released++; free(storage); }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExhaustiveSmallAndSeededPrefixesMatchHostOracleWithLogarithmicCalls(int optimization)
    {
        var random = new Random(731627);
        var samples = new List<int[]>();
        for (int length = 0; length <= 12; length++)
            for (int boundary = 0; boundary <= length; boundary++)
                samples.Add(Enumerable.Range(0, length)
                    .Select(index => index < boundary ? random.Next(1, 100) : random.Next(-100, 1)).ToArray());
        foreach (int length in new[] { 15, 16, 17, 31, 32, 33, 63, 64, 65, 127, 128, 129 })
        {
            foreach (int boundary in new[] { 0, 1, length / 2, length - 1, length })
                samples.Add(Enumerable.Range(0, length)
                    .Select(index => index < boundary ? random.Next(1, 100) : random.Next(-100, 1)).ToArray());
            samples.Add(Enumerable.Range(0, length).Select(_ => random.Next(-100, 101))
                .OrderByDescending(value => value > 0).ToArray());
        }
        var packet = new List<int>();
        foreach (int[] sample in samples)
        {
            int expected = sample.TakeWhile(value => value > 0).Count();
            int limit = sample.Length == 0 ? 0 : System.Numerics.BitOperations.Log2((uint)sample.Length) + 1;
            packet.AddRange([sample.Length, expected, limit]); packet.AddRange(sample);
        }
        Run(Allocator + $$"""
            int calls = 0;
            bool Positive(immutable(int)* value) { calls++; return *value > 0; }
            int main() {
                int[{{packet.Count}}] packet = [{{string.Join(',', packet)}}]; nuint at = 0;
                int baseline = attempts; deny = true;
                for (int row = 0; row < {{samples.Count}}; row++) {
                    nuint count = (nuint)packet[at++]; nuint expected = (nuint)packet[at++];
                    int limit = packet[at++]; int[129] data; int[129] saved;
                    for (nuint i = 0; i < count; i++) { data[i] = packet[at++]; saved[i] = data[i]; }
                    Span<int> values = new(data, count); calls = 0;
                    if (PartitionPoint<int>(values.AsImmutable(), Positive) != expected) return 1;
                    if (calls > limit || (count == 0 && calls != 0) || (count != 0 && calls == 0)) return 2;
                    for (nuint i = 0; i < count; i++) if (data[i] != saved[i]) return 3;
                }
                if (at != {{packet.Count}} || attempts != baseline || allocated != released) return 4;
                deny = false; return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyNeverReadsStorageAndSingletonMakesOneBorrowedCall(int optimization)
        => Run(Allocator + """
            int calls = 0;
            bool Unexpected(immutable(int)* value) { calls++; throw new Exception("empty input read"); }
            bool Positive(immutable(int)* value) { calls++; return *value > 0; }
            int main() {
                Span<immutable(int)> empty = new();
                Span<immutable(int)> unreadable = new(((nuint)1).reinterpret<immutable(int)*>(), 0);
                int[1] data = [5]; Span<int> singleton = new(data, 1);
                int baseline = attempts; deny = true;
                if (PartitionPoint<int>(empty, Unexpected) != 0 || PartitionPoint<int>(unreadable, Unexpected) != 0 || calls != 0) return 1;
                if (PartitionPoint<int>(singleton.AsImmutable(), Positive) != 1 || calls != 1) return 2;
                data[0] = 0; calls = 0;
                if (PartitionPoint<int>(singleton.AsImmutable(), Positive) != 0 || calls != 1) return 3;
                if (data[0] != 0 || attempts != baseline) return 4;
                deny = false; return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableOwnersAreBorrowedWithoutAllocationAndEveryPredicateFailurePreservesInput(int optimization)
        => Run(Allocator + """
            int made = 0; int dead = 0; int calls = 0; int boundary = 0; int failAt = -1;
            immutable(Owner)* origin = ((nuint)1).reinterpret<immutable(Owner)*>();
            bool[9] visited;
            struct Owner {
                public int Id; public String Token;
                public Owner(int id) { Id = id; Token = s"owner"; made++; }
                ~Owner() { dead++; }
            }
            bool BeforeBoundary(immutable(Owner)* value) {
                if (value.Id < 0 || value.Id >= 9 || value != origin + value.Id) throw new Exception("not borrowed", 99);
                nuint index = (nuint)value.Id;
                if (visited[index]) throw new Exception("duplicate probe", 98);
                visited[index] = true;
                int position = calls; calls++;
                if (position == failAt) throw new Exception("original predicate", 77);
                return value.Id < boundary;
            }
            void Reset() { calls = 0; for (nuint i = 0; i < 9; i++) visited[i] = false; }
            int main() {
                {
                    Vector<Owner> owners = new(); owners.Reserve(9);
                    for (int id = 0; id < 9; id++) owners.Add(new Owner(id));
                    origin = &owners[0]; nuint[9] tokens;
                    for (nuint i = 0; i < 9; i++) tokens[i] = (owners[i].Token.Data).reinterpret<nuint>();
                    int live = allocated - released; nuint capacity = owners.Capacity;
                    for (int split = 0; split <= 9; split++) {
                        boundary = split; failAt = -1; Reset(); int baseline = attempts; deny = true;
                        if (PartitionPoint<Owner>(owners.AsImmutable(), BeforeBoundary) != (nuint)split || calls > 4) return 1;
                        deny = false; if (attempts != baseline) return 2;
                        int totalCalls = calls;
                        for (int failure = 0; failure < totalCalls; failure++) {
                            Reset(); failAt = failure; nuint result = 99; bool caught = false;
                            try { result = PartitionPoint<Owner>(owners.AsImmutable(), BeforeBoundary); }
                            catch (ref Exception error) {
                                caught = error.GetCode() == 77 && CString::Equals(error.GetMessage(), "original predicate");
                            }
                            if (!caught || result != 99 || calls != failure + 1) return 3;
                            if (made != 9 || dead != 0 || owners.Count != 9 || owners.Capacity != capacity || allocated - released != live) return 4;
                            for (nuint i = 0; i < 9; i++)
                                if (&owners[i] != origin + i || owners[i].Id != (int)i ||
                                    (owners[i].Token.Data).reinterpret<nuint>() != tokens[i] || owners[i].Token.Length != 5) return 5;
                        }
                    }
                }
                if (made != 9 || dead != 9 || allocated != released) return 6;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MaintainedDocumentationExampleRuns(int optimization)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Guides/Standard/core/Collections/ALGORITHMS.md");
        string section = File.ReadAllText(path).Split("## Partition boundary search")[1].Split("## Adjacent pair search")[0];
        string program = section.Split("```csharp")[1].Split("```")[0];
        Run(program, optimization);
    }

    private static void Run(string program, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("partition-point.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"PartitionPoint exit {result.ExitCode}: {result.StandardError}");
    }
}
