namespace gflat.Tests.Std.Collections;

public sealed class AllOfTests
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
    public void BoundaryAndSeededInputsMatchHostWithExactShortCircuitTrace(int optimization)
    {
        List<int[]> samples = [[], [0], [1], [-1], [1, 2, 3], [0, 1, 2], [1, 2, 0], [1, 0, 1],
            Enumerable.Repeat(1, 129).ToArray(), Enumerable.Repeat(1, 128).Append(0).ToArray()];
        var random = new Random(619327);
        for (int sample = 0; sample < 24; sample++)
        {
            int length = random.Next(0, 66);
            int firstFalse = random.Next(0, length + 1);
            samples.Add(Enumerable.Range(0, length).Select(index => index < firstFalse ? random.Next(1, 20) : random.Next(-2, 3)).ToArray());
        }
        var packet = new List<int>();
        foreach (var sample in samples)
        {
            int firstFalse = Array.FindIndex(sample, value => value <= 0);
            packet.AddRange([sample.Length, Array.TrueForAll(sample, value => value > 0) ? 1 : 0,
                firstFalse < 0 ? sample.Length : firstFalse + 1]);
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
                    nuint count = (nuint)packet[at++]; bool expected = packet[at++] != 0; int expectedCalls = packet[at++];
                    int[129] data; int[129] saved;
                    for (nuint i = 0; i < count; i++) { data[i] = packet[at++]; saved[i] = data[i]; }
                    Span<int> values = new(data, count); origin = data; calls = 0;
                    if (AllOf<int>(values.AsImmutable(), Positive) != expected || calls != expectedCalls) return 1;
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
    public void EmptySingletonAndFalseBeforeThrowingSuffixFollowShortCircuitContract(int optimization)
        => Run(Allocator + """
            int calls = 0;
            bool Unexpected(immutable(int)* value) { calls++; throw new Exception("empty input read"); }
            bool Positive(immutable(int)* value) { calls++; return *value > 0; }
            bool Check(immutable(int)* value) {
                calls++; if (*value == 99) throw new Exception("unreachable suffix", 77); return *value > 0;
            }
            int main() {
                Span<immutable(int)> empty = new(); Span<immutable(int)> unreadable = new((immutable(int)*)1, 0);
                int[1] data = [5]; Span<int> singleton = new(data, 1);
                int baseline = attempts; deny = true;
                if (!AllOf<int>(empty, Unexpected) || !AllOf<int>(unreadable, Unexpected) || calls != 0) return 1;
                if (!AllOf<int>(singleton.AsImmutable(), Positive) || calls != 1) return 2;
                data[0] = 0; calls = 0;
                if (AllOf<int>(singleton.AsImmutable(), Positive) || calls != 1 || data[0] != 0) return 3;
                int[3] mixed = [1, 0, 99]; Span<int> values = new(mixed, 3); calls = 0;
                if (AllOf<int>(values.AsImmutable(), Check) || calls != 2 || mixed[2] != 99) return 4;
                if (attempts != baseline) return 5; deny = false; return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableOwnersAreBorrowedWithoutAllocationAndOriginalFailuresPreserveInput(int optimization)
        => Run(Allocator + """
            int made = 0; int dead = 0; int calls = 0; int firstFalse = -1; int failAt = -1;
            immutable(Owner)* origin = (immutable(Owner)*)1;
            struct Owner {
                public int Id; public String Token;
                public Owner(int id) { Id = id; Token = s"owner"; made++; }
                ~Owner() { dead++; }
            }
            bool Ready(immutable(Owner)* value) {
                if (value != origin + calls || value.Id != calls) throw new Exception("not borrowed in order", 99);
                int index = calls; calls++; if (index == failAt) throw new Exception("original predicate", 77);
                return value.Id != firstFalse;
            }
            int main() {
                {
                    Vector<Owner> owners = new(); owners.Reserve(7);
                    for (int id = 0; id < 7; id++) owners.Add(new Owner(id));
                    origin = &owners[0]; nuint[7] tokens;
                    for (nuint i = 0; i < 7; i++) tokens[i] = (nuint)owners[i].Token.Data;
                    int live = allocated - released; nuint capacity = owners.Capacity; int[4] stops = [-1, 0, 3, 6];
                    for (nuint pattern = 0; pattern < 4; pattern++) {
                        firstFalse = stops[pattern]; bool expected = firstFalse < 0;
                        int expectedCalls = 7; if (!expected) expectedCalls = firstFalse + 1;
                        int baseline = attempts; calls = 0; failAt = -1; deny = true;
                        if (AllOf<Owner>(owners.AsImmutable(), Ready) != expected || calls != expectedCalls) return 1;
                        calls = 0; failAt = expectedCalls;
                        if (AllOf<Owner>(owners.AsImmutable(), Ready) != expected || calls != expectedCalls) return 2;
                        deny = false; if (attempts != baseline) return 3;
                        for (int failure = 0; failure < expectedCalls; failure++) {
                            calls = 0; failAt = failure; bool result = true; bool caught = false;
                            try { result = AllOf<Owner>(owners.AsImmutable(), Ready); }
                            catch (ref Exception error) {
                                caught = error.GetCode() == 77 && CString::Equals(error.GetMessage(), "original predicate");
                            }
                            if (!caught || !result || calls != failure + 1) return 4;
                            if (made != 7 || dead != 0 || owners.Count != 7 || owners.Capacity != capacity || allocated - released != live) return 5;
                            for (nuint i = 0; i < 7; i++)
                                if (&owners[i] != origin + i || owners[i].Id != (int)i ||
                                    (nuint)owners[i].Token.Data != tokens[i] || owners[i].Token.Length != 5) return 6;
                        }
                    }
                }
                if (made != 7 || dead != 7 || allocated != released) return 7; return 0;
            }
            """, optimization);

    private static void Run(string program, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("all-of.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"AllOf exit {result.ExitCode}: {result.StandardError}");
    }
}
