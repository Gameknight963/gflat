namespace gflat.Tests.Std.Collections;

public sealed class PredicateQuantifierTests
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
    public void SeededAndBoundaryCasesMatchHostWithExactAscendingShortCircuitTrace(int optimization)
    {
        List<int[]> samples = [[], [0], [1], [-1], [0, 0, 0], [1, 0, 1], [0, 0, 1], [0, 1, 0],
            Enumerable.Repeat(0, 65).ToArray(), Enumerable.Repeat(0, 64).Append(1).ToArray()];
        var random = new Random(857321);
        for (int sample = 0; sample < 16; sample++)
        {
            int length = random.Next(0, 49);
            int prefix = random.Next(0, length + 1);
            samples.Add(Enumerable.Range(0, length).Select(index => index < prefix ? random.Next(-20, 1) : random.Next(-2, 3)).ToArray());
        }
        var packet = new List<int>();
        foreach (var sample in samples)
        {
            int first = Array.FindIndex(sample, value => value > 0);
            packet.AddRange([sample.Length, sample.Any(value => value > 0) ? 1 : 0,
                first < 0 ? sample.Length : first + 1]);
            packet.AddRange(sample);
        }
        Run(Allocator + $$"""
            int calls = 0; immutable(int)* origin = ((nuint)1).reinterpret<immutable(int)*>();
            bool Positive(immutable(int)* value) {
                if (value != origin + calls) throw new Exception("wrong callback order", 99);
                calls++; return *value > 0;
            }
            int main() {
                int[{{packet.Count}}] packet = [{{string.Join(',', packet)}}]; nuint at = 0;
                int baseline = attempts; deny = true;
                for (int row = 0; row < {{samples.Count}}; row++) {
                    nuint count = (nuint)packet[at++]; bool any = packet[at++] != 0; int expectedCalls = packet[at++];
                    int[65] data; int[65] saved;
                    for (nuint i = 0; i < count; i++) { data[i] = packet[at++]; saved[i] = data[i]; }
                    Span<int> values = new(data, count); origin = data; calls = 0;
                    if (AnyOf<int>(values.AsImmutable(), Positive) != any || calls != expectedCalls) return 1;
                    calls = 0;
                    if (NoneOf<int>(values.AsImmutable(), Positive) == any || calls != expectedCalls) return 2;
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
    public void EmptyUnreadableViewsAndUnreachableThrowingSuffixAreNeverRead(int optimization)
        => Run(Allocator + """
            int calls = 0;
            bool Unexpected(immutable(int)* value) { calls++; throw new Exception("empty input read"); }
            bool Check(immutable(int)* value) {
                calls++; if (*value == 99) throw new Exception("unreachable suffix", 77); return *value > 0;
            }
            int main() {
                Span<immutable(int)> empty = new(); Span<immutable(int)> unreadable = new(((nuint)1).reinterpret<immutable(int)*>(), 0);
                int baseline = attempts; deny = true;
                if (AnyOf<int>(empty, Unexpected) || AnyOf<int>(unreadable, Unexpected) ||
                    !NoneOf<int>(empty, Unexpected) || !NoneOf<int>(unreadable, Unexpected) || calls != 0) return 1;
                int[3] data = [0, 1, 99]; Span<int> values = new(data, 3);
                if (!AnyOf<int>(values.AsImmutable(), Check) || calls != 2) return 2;
                calls = 0;
                if (NoneOf<int>(values.AsImmutable(), Check) || calls != 2 || data[2] != 99) return 3;
                if (attempts != baseline) return 4; deny = false; return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableOwnersAreBorrowedAndEveryReachedExceptionPreservesOwnersAndResult(int optimization)
        => Run(Allocator + """
            int made = 0; int dead = 0; int calls = 0; int match = -1; int failAt = -1;
            immutable(Owner)* origin = ((nuint)1).reinterpret<immutable(Owner)*>();
            struct Owner {
                public int Id; public String Token;
                public Owner(int id) { Id = id; Token = s"owner"; made++; }
                ~Owner() { dead++; }
            }
            bool Matches(immutable(Owner)* value) {
                if (value != origin + calls || value.Id != calls) throw new Exception("not borrowed in order", 99);
                int index = calls; calls++; if (index == failAt) throw new Exception("original predicate", 77);
                return value.Id == match;
            }
            int main() {
                {
                    Vector<Owner> owners = new(); owners.Reserve(7);
                    for (int id = 0; id < 7; id++) owners.Add(new Owner(id));
                    origin = &owners[0]; nuint[7] tokens;
                    for (nuint i = 0; i < 7; i++) tokens[i] = (owners[i].Token.Data).reinterpret<nuint>();
                    int live = allocated - released; nuint capacity = owners.Capacity; int[4] matches = [-1, 0, 3, 6];
                    for (nuint pattern = 0; pattern < 4; pattern++) {
                        match = matches[pattern]; bool any = match >= 0; int expectedCalls = 7;
                        if (any) expectedCalls = match + 1;
                        for (int mode = 0; mode < 2; mode++) {
                            int baseline = attempts; calls = 0; failAt = -1; deny = true; bool result = false;
                            if (mode == 0) result = AnyOf<Owner>(owners.AsImmutable(), Matches);
                            else result = NoneOf<Owner>(owners.AsImmutable(), Matches);
                            bool expected = any; if (mode != 0) expected = !any;
                            deny = false;
                            if (result != expected || calls != expectedCalls || attempts != baseline) return 1;
                            for (int failure = 0; failure < expectedCalls; failure++) {
                                calls = 0; failAt = failure; result = true; bool caught = false;
                                try {
                                    if (mode == 0) result = AnyOf<Owner>(owners.AsImmutable(), Matches);
                                    else result = NoneOf<Owner>(owners.AsImmutable(), Matches);
                                } catch (ref Exception error) {
                                    caught = error.GetCode() == 77 && CString::Equals(error.GetMessage(), "original predicate");
                                }
                                if (!caught || !result || calls != failure + 1) return 2;
                                if (made != 7 || dead != 0 || owners.Count != 7 || owners.Capacity != capacity || allocated - released != live) return 3;
                                for (nuint i = 0; i < 7; i++)
                                    if (&owners[i] != origin + i || owners[i].Id != (int)i ||
                                        (owners[i].Token.Data).reinterpret<nuint>() != tokens[i] || owners[i].Token.Length != 5) return 4;
                            }
                        }
                    }
                }
                if (made != 7 || dead != 7 || allocated != released) return 5; return 0;
            }
            """, optimization);

    private static void Run(string program, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("predicate-quantifiers.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Predicate quantifier exit {result.ExitCode}: {result.StandardError}");
    }
}
