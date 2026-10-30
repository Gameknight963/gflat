namespace gflat.Tests.Std.Collections;

public sealed class IsPartitionedTests
{
    private const string Allocator = """
        using std; using std::Text; using std::Memory; using std::Collections;
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
    public void ExhaustiveAndSeededMasksMatchOracleWithExactAscendingShortCircuitTrace(int optimization)
    {
        var samples = new List<int[]>();
        for (int length = 0; length <= 8; length++)
            for (int mask = 0; mask < 1 << length; mask++)
                samples.Add(Enumerable.Range(0, length).Select(index => (mask & 1 << index) == 0 ? -1 : 1).ToArray());
        var random = new Random(172739);
        foreach (int length in new[] { 15, 16, 31, 32, 63, 64, 127, 129 })
        {
            for (int sample = 0; sample < 6; sample++)
                samples.Add(Enumerable.Range(0, length).Select(_ => random.Next(-100, 101)).ToArray());
            foreach (int boundary in new[] { 0, 1, length / 2, length - 1, length })
                samples.Add(Enumerable.Range(0, length).Select(index => index < boundary ? 2 : 0).ToArray());
        }
        var packet = new List<int>();
        foreach (var sample in samples)
        {
            int firstFalse = Array.FindIndex(sample, value => value <= 0);
            int violation = firstFalse < 0 ? -1 : Array.FindIndex(sample, firstFalse, value => value > 0);
            bool expected = violation < 0;
            packet.AddRange([sample.Length, expected ? 1 : 0, expected ? sample.Length : violation + 1,
                firstFalse < 0 ? sample.Length : firstFalse]);
            packet.AddRange(sample);
        }
        Run(Allocator + $$"""
            int calls = 0; immutable(int)* origin = ((nuint)1).reinterpret<immutable(int)*>();
            bool Positive(immutable(int)* value) {
                if (value != origin + calls) throw new Exception("wrong callback order", 99);
                calls++; return *value > 0;
            }
            bool StablePositive(immutable(int)* value) => *value > 0;
            int main() {
                int[{{packet.Count}}] packet = [{{string.Join(',', packet)}}]; nuint at = 0;
                int baseline = attempts; deny = true;
                for (int row = 0; row < {{samples.Count}}; row++) {
                    nuint count = (nuint)packet[at++]; bool expected = packet[at++] != 0;
                    int expectedCalls = packet[at++]; nuint boundary = (nuint)packet[at++];
                    int[129] data; int[129] saved;
                    for (nuint i = 0; i < count; i++) { data[i] = packet[at++]; saved[i] = data[i]; }
                    Span<int> values = new(data, count); origin = data; calls = 0;
                    if (IsPartitioned<int>(values.AsImmutable(), Positive) != expected || calls != expectedCalls) return 1;
                    if (expected && PartitionPoint<int>(values.AsImmutable(), StablePositive) != boundary) return 2;
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
    public void EmptyNeverReadsStorageAndSingletonMakesOneCall(int optimization)
        => Run(Allocator + """
            int calls = 0;
            bool Unexpected(immutable(int)* value) { calls++; throw new Exception("empty input read"); }
            bool Positive(immutable(int)* value) { calls++; return *value > 0; }
            int main() {
                Span<immutable(int)> empty = new();
                Span<immutable(int)> unreadable = new(((nuint)1).reinterpret<immutable(int)*>(), 0);
                int[1] data = [5]; Span<int> singleton = new(data, 1);
                int baseline = attempts; deny = true;
                if (!IsPartitioned<int>(empty, Unexpected) || !IsPartitioned<int>(unreadable, Unexpected) || calls != 0) return 1;
                if (!IsPartitioned<int>(singleton.AsImmutable(), Positive) || calls != 1) return 2;
                data[0] = 0; calls = 0;
                if (!IsPartitioned<int>(singleton.AsImmutable(), Positive) || calls != 1 || data[0] != 0) return 3;
                if (attempts != baseline) return 4; deny = false; return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableOwnersAreBorrowedAndFailuresPreserveEveryOwner(int optimization)
        => Run(Allocator + """
            int made = 0; int dead = 0; int calls = 0; int mask = 0; int failAt = -1;
            immutable(Owner)* origin = ((nuint)1).reinterpret<immutable(Owner)*>();
            struct Owner {
                public int Id; public String Token;
                public Owner(int id) { Id = id; Token = s"owner"; made++; }
                ~Owner() { dead++; }
            }
            bool Matches(immutable(Owner)* value) {
                if (value != origin + calls || value.Id != calls) throw new Exception("not borrowed in order", 99);
                int index = calls; calls++;
                if (index == failAt) throw new Exception("original predicate", 77);
                return (mask & (1 << value.Id)) != 0;
            }
            int main() {
                {
                    Vector<Owner> owners = new(); owners.Reserve(9);
                    for (int id = 0; id < 9; id++) owners.Add(new Owner(id));
                    origin = &owners[0]; nuint[9] tokens;
                    for (nuint i = 0; i < 9; i++) tokens[i] = (owners[i].Token.Data).reinterpret<nuint>();
                    int live = allocated - released; nuint capacity = owners.Capacity;
                    int[5] masks = [511, 0, 31, 85, 2];
                    for (nuint pattern = 0; pattern < 5; pattern++) {
                        mask = masks[pattern]; bool expected = true; bool seenFalse = false; int expectedCalls = 9;
                        for (int i = 0; i < 9; i++) {
                            bool matched = (mask & (1 << i)) != 0;
                            if (!matched) seenFalse = true;
                            else if (seenFalse) { expected = false; expectedCalls = i + 1; break; }
                        }
                        int baseline = attempts; calls = 0; failAt = -1; deny = true;
                        if (IsPartitioned<Owner>(owners.AsImmutable(), Matches) != expected || calls != expectedCalls) return 1;
                        // A later throwing callback must not run after the first violation.
                        calls = 0; failAt = expectedCalls;
                        if (IsPartitioned<Owner>(owners.AsImmutable(), Matches) != expected || calls != expectedCalls) return 2;
                        deny = false; if (attempts != baseline) return 3;
                        for (int failure = 0; failure < expectedCalls; failure++) {
                            calls = 0; failAt = failure; bool caught = false; bool completed = false;
                            try { IsPartitioned<Owner>(owners.AsImmutable(), Matches); completed = true; }
                            catch (ref Exception error) {
                                caught = error.GetCode() == 77 && CString::Equals(error.GetMessage(), "original predicate");
                            }
                            if (!caught || completed || calls != failure + 1) return 4;
                            if (made != 9 || dead != 0 || owners.Count != 9 || owners.Capacity != capacity || allocated - released != live) return 5;
                            for (nuint i = 0; i < 9; i++)
                                if (&owners[i] != origin + i || owners[i].Id != (int)i ||
                                    (owners[i].Token.Data).reinterpret<nuint>() != tokens[i] || owners[i].Token.Length != 5) return 6;
                        }
                    }
                }
                if (made != 9 || dead != 9 || allocated != released) return 7; return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MaintainedDocumentationExampleRuns(int optimization)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Guides/std/core/Collections/ALGORITHMS.md");
        string section = File.ReadAllText(path).Split("## Partition validation")[1].Split("## Partition boundary search")[0];
        string program = section.Split("```csharp")[1].Split("```")[0];
        Run(program, optimization);
    }

    private static void Run(string program, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("is-partitioned.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"IsPartitioned exit {result.ExitCode}: {result.StandardError}");
    }
}
