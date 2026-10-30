namespace gflat.Tests.Std.Collections;

public sealed class AdjacentFindTests
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
    public void SeededAdjacentZipOracleChecksDirectionEarlyExitAndNoMutation(int optimization)
    {
        List<int[]> samples = [[], [7], [1, 1], [1, 2, 2, 3], [3, 2, 1], [1, 2, 3],
            [int.MinValue, int.MaxValue, int.MaxValue], [4, 4, 4, 4]];
        var random = new Random(681295);
        for (int row = 0; row < 48; row++)
            samples.Add(Enumerable.Range(0, random.Next(0, 33)).Select(_ => random.Next(-12, 13)).ToArray());
        var packet = new List<int>();
        foreach (int[] sample in samples)
        {
            // The oracle forms adjacent pairs on the host, independently of the span implementation.
            int expected = sample.Zip(sample.Skip(1)).Select((pair, index) => (pair, index))
                .Where(item => item.pair.First >= item.pair.Second).Select(item => item.index)
                .DefaultIfEmpty(sample.Length).First();
            packet.AddRange([sample.Length, expected]); packet.AddRange(sample);
        }
        Run(Allocator + $$"""
            int calls = 0; int[32] lefts; int[32] rights;
            bool Match(immutable(int)* left, immutable(int)* right) {
                if (calls >= 32) throw new Exception("too many calls");
                lefts[(nuint)calls] = *left; rights[(nuint)calls] = *right; calls++;
                return *left >= *right;
            }
            int main() {
                int[{{packet.Count}}] packet = [{{string.Join(',', packet)}}]; nuint at = 0;
                int baseline = attempts; deny = true;
                for (int row = 0; row < {{samples.Count}}; row++) {
                    nuint count = (nuint)packet[at++]; nuint expected = (nuint)packet[at++];
                    int[32] data; int[32] saved;
                    for (nuint i = 0; i < count; i++) { data[i] = packet[at++]; saved[i] = data[i]; }
                    Span<int> values = new(data, count); calls = 0;
                    if (AdjacentFind<int>(values.AsImmutable(), Match) != expected) return 1;
                    nuint expectedCalls = 0;
                    if (count > 1) { expectedCalls = count - 1; if (expected < count) expectedCalls = expected + 1; }
                    if (calls != (int)expectedCalls) return 2;
                    for (nuint i = 0; i < expectedCalls; i++)
                        if (lefts[i] != saved[i] || rights[i] != saved[i + 1]) return 3;
                    for (nuint i = 0; i < count; i++) if (data[i] != saved[i]) return 4;
                }
                if (attempts != baseline || allocated != released) return 5;
                deny = false; return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyAndSingletonNeverReadStorageOrInvokeCallback(int optimization)
        => Run(Allocator + """
            int calls = 0;
            bool Unexpected(immutable(int)* left, immutable(int)* right) {
                calls++; throw new Exception("unexpected callback");
            }
            int main() {
                Span<immutable(int)> empty = new();
                Span<immutable(int)> unreadableEmpty = new(((nuint)1).reinterpret<immutable(int)*>(), 0);
                Span<immutable(int)> unreadableSingleton = new(((nuint)1).reinterpret<immutable(int)*>(), 1);
                int baseline = attempts; deny = true;
                if (AdjacentFind<int>(empty, Unexpected) != 0) return 1;
                if (AdjacentFind<int>(unreadableEmpty, Unexpected) != 0) return 2;
                if (AdjacentFind<int>(unreadableSingleton, Unexpected) != 1) return 3;
                if (AdjacentFind<int>(empty) != 0 || AdjacentFind<int>(unreadableSingleton) != 1) return 4;
                if (calls != 0 || attempts != baseline) return 5;
                deny = false; return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableOwnersAreBorrowedAndEachOriginalFailureLeavesSourceUnchanged(int optimization)
        => Run(Allocator + """
            int made = 0; int dead = 0; int calls = 0; int failAt = -1;
            immutable(Owner)* origin = ((nuint)1).reinterpret<immutable(Owner)*>();
            struct Owner {
                public int Id; public String Token;
                public Owner(int id) { Id = id; Token = s"owner"; made++; }
                ~Owner() { dead++; }
            }
            bool Match(immutable(Owner)* left, immutable(Owner)* right) {
                if (left != origin + calls || right != left + 1) throw new Exception("not borrowed", 99);
                int position = calls; calls++;
                if (position == failAt) throw new Exception("original predicate", 77);
                return left.Id == right.Id;
            }
            int main() {
                {
                    Vector<Owner> owners = new();
                    for (int id = 0; id < 5; id++) owners.Add(new Owner(id));
                    origin = &owners[0]; nuint[5] tokens;
                    for (nuint i = 0; i < 5; i++) tokens[i] = (owners[i].Token.Data).reinterpret<nuint>();
                    int baseline = attempts; int live = allocated - released; deny = true;
                    if (AdjacentFind<Owner>(owners.AsImmutable(), Match) != 5 || calls != 4) return 1;
                    deny = false;
                    if (attempts != baseline) return 2;
                    for (int failure = 0; failure < 4; failure++) {
                        calls = 0; failAt = failure; nuint result = 99; bool caught = false;
                        try { result = AdjacentFind<Owner>(owners.AsImmutable(), Match); }
                        catch (ref Exception error) {
                            caught = error.GetCode() == 77 && CString::Equals(error.GetMessage(), "original predicate");
                        }
                        if (!caught || result != 99 || calls != failure + 1) return 3;
                        if (made != 5 || dead != 0 || owners.Count != 5 || allocated - released != live) return 4;
                        for (nuint i = 0; i < 5; i++)
                            if (&owners[i] != origin + i || owners[i].Id != (int)i ||
                                (owners[i].Token.Data).reinterpret<nuint>() != tokens[i] || owners[i].Token.Length != 5) return 5;
                    }
                }
                if (made != dead || allocated != released) return 6;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DefaultsUsePrimitiveAndCountedStringEqualityContracts(int optimization)
        => Run(Allocator + """
            bool SamePrefix(immutable(String)* left, immutable(String)* right)
                => left.Data[0] == right.Data[0];
            int main() {
                int[5] data = [1, 2, 2, 4, 3]; Span<int> values = new(data, 5);
                String[4] text = [s"a\0b", s"a", s"a\0b", s"a\0b"];
                Span<String> strings = new(text, 4);
                double nan = 0.0 / 0.0; double[4] numbers = [1.0, nan, nan, 2.0];
                Span<double> floats = new(numbers, 4);
                double[2] zeros = [-0.0, 0.0]; Span<double> signedZeros = new(zeros, 2);
                int baseline = attempts; deny = true;
                if (AdjacentFind<int>(values.AsImmutable()) != 1) return 1;
                if (AdjacentFind<String>(strings.AsImmutable()) != 2) return 2;
                if (AdjacentFind<String>(strings.AsImmutable(), SamePrefix) != 0) return 3;
                if (AdjacentFind<double>(floats.AsImmutable()) != 1) return 4;
                if (AdjacentFind<double>(signedZeros.AsImmutable()) != 0) return 5;
                if (attempts != baseline) return 6;
                deny = false; return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MaintainedDocumentationExampleRuns(int optimization)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Guides/Standard/core/Collections/ALGORITHMS.md");
        string documentation = File.ReadAllText(path);
        string section = documentation.Split("## Adjacent pair search")[1].Split("## Reverse predicate search")[0];
        string program = section.Split("```csharp")[1].Split("```")[0];
        Run(program, optimization);
    }

    private static void Run(string program, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("adjacent-find.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"AdjacentFind exit {result.ExitCode}: {result.StandardError}");
    }
}
