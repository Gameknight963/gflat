namespace gflat.Tests.Std.Collections;

public sealed class CountIfTests
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
    public void SeededAndBoundaryInputsMatchHostCountsWithCompleteAscendingTrace(int optimization)
    {
        List<int[]> samples = [[], [0], [1], [-1], [int.MinValue, int.MaxValue, 0, 1, -1],
            Enumerable.Repeat(1, 129).ToArray(), new int[129],
            Enumerable.Range(0, 129).Select(index => index % 2 == 0 ? 1 : -1).ToArray()];
        var random = new Random(417623);
        for (int sample = 0; sample < 24; sample++)
            samples.Add(Enumerable.Range(0, random.Next(0, 66)).Select(_ => random.Next(-20, 21)).ToArray());
        var packet = new List<int>();
        foreach (var sample in samples)
        {
            packet.AddRange([sample.Length, sample.Count(value => value > 0)]);
            packet.AddRange(sample);
        }
        Run(Allocator + $$"""
            int calls = 0; immutable(int)* origin = (immutable(int)*)1;
            bool Positive(immutable(int)* value) {
                if (value != origin + calls) throw new Exception("wrong callback order", 99);
                calls++; return *value > 0;
            }
            int main() {
                int[{{packet.Count}}] packet = [{{string.Join(',', packet)}}]; nuint at = 0;
                int baseline = attempts; deny = true;
                for (int row = 0; row < {{samples.Count}}; row++) {
                    nuint count = (nuint)packet[at++]; nuint expected = (nuint)packet[at++];
                    int[129] data; int[129] saved;
                    for (nuint i = 0; i < count; i++) { data[i] = packet[at++]; saved[i] = data[i]; }
                    Span<int> values = new(data, count); origin = data; calls = 0;
                    if (CountIf<int>(values.AsImmutable(), Positive) != expected || calls != (int)count) return 1;
                    for (nuint i = 0; i < count; i++) if (data[i] != saved[i]) return 2;
                }
                if (at != {{packet.Count}} || attempts != baseline || allocated != released) return 3;
                deny = false; return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyNeverReadsStorageAndSingletonCallsOnce(int optimization)
        => Run(Allocator + """
            int calls = 0;
            bool Unexpected(immutable(int)* value) { calls++; throw new Exception("empty input read"); }
            bool Positive(immutable(int)* value) { calls++; return *value > 0; }
            int main() {
                Span<immutable(int)> empty = new();
                Span<immutable(int)> unreadable = new((immutable(int)*)1, 0);
                int[1] data = [5]; Span<int> singleton = new(data, 1);
                int baseline = attempts; deny = true;
                if (CountIf<int>(empty, Unexpected) != 0 || CountIf<int>(unreadable, Unexpected) != 0 || calls != 0) return 1;
                if (CountIf<int>(singleton.AsImmutable(), Positive) != 1 || calls != 1) return 2;
                data[0] = 0; calls = 0;
                if (CountIf<int>(singleton.AsImmutable(), Positive) != 0 || calls != 1 || data[0] != 0) return 3;
                if (attempts != baseline) return 4; deny = false; return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableOwnersAreBorrowedWithoutAllocationAndExceptionsPreserveInput(int optimization)
        => Run(Allocator + """
            int made = 0; int dead = 0; int calls = 0; int failAt = -1;
            immutable(Owner)* origin = (immutable(Owner)*)1;
            struct Owner {
                public int Id; public String Token;
                public Owner(int id) { Id = id; Token = s"owner"; made++; }
                ~Owner() { dead++; }
            }
            bool Even(immutable(Owner)* value) {
                if (value != origin + calls || value.Id != calls) throw new Exception("not borrowed in order", 99);
                int index = calls; calls++;
                if (index == failAt) throw new Exception("original predicate", 77);
                return value.Id % 2 == 0;
            }
            int main() {
                {
                    Vector<Owner> owners = new(); owners.Reserve(7);
                    for (int id = 0; id < 7; id++) owners.Add(new Owner(id));
                    origin = &owners[0]; nuint[7] tokens;
                    for (nuint i = 0; i < 7; i++) tokens[i] = (nuint)owners[i].Token.Data;
                    int live = allocated - released; nuint capacity = owners.Capacity;
                    int baseline = attempts; deny = true;
                    if (CountIf<Owner>(owners.AsImmutable(), Even) != 4 || calls != 7) return 1;
                    deny = false; if (attempts != baseline) return 2;
                    for (int failure = 0; failure < 7; failure++) {
                        calls = 0; failAt = failure; nuint result = 99; bool caught = false;
                        try { result = CountIf<Owner>(owners.AsImmutable(), Even); }
                        catch (ref Exception error) {
                            caught = error.GetCode() == 77 && CString::Equals(error.GetMessage(), "original predicate");
                        }
                        if (!caught || result != 99 || calls != failure + 1) return 3;
                        if (made != 7 || dead != 0 || owners.Count != 7 || owners.Capacity != capacity || allocated - released != live) return 4;
                        for (nuint i = 0; i < 7; i++)
                            if (&owners[i] != origin + i || owners[i].Id != (int)i ||
                                (nuint)owners[i].Token.Data != tokens[i] || owners[i].Token.Length != 5) return 5;
                    }
                }
                if (made != 7 || dead != 7 || allocated != released) return 6; return 0;
            }
            """, optimization);

    private static void Run(string program, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("count-if.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"CountIf exit {result.ExitCode}: {result.StandardError}");
    }
}
