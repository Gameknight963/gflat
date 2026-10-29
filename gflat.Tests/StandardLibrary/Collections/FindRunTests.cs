namespace gflat.Tests.Std.Collections;

public sealed class FindRunTests
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

    [Fact]
    public void ExistingEqualitySubsetChecksWithoutTheUnusedRunSearchSource()
    {
        string[] names = ["core/Exception.gf", "core/IEquatable.gf", "core/Collections/IEnumerable.gf",
            "core/Queries/Enumerable.gf", "core/Memory/Span.gf", "core/Collections/EqualityComparer.gf",
            "core/Collections/span_algorithms.gf", "core/Collections/default_equality.gf"];
        var files = names.Select(name => Path.Combine(AppContext.BaseDirectory, "Std", name))
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("old-equality-subset.gf", """
                using std; using std::Memory; using std::Collections;
                namespace Memory {
                    public void*? Allocate(nuint bytes) => null;
                    public void Free(void* storage) nothrow { }
                }
                int main() {
                    int[3] data = [1, 2, 2]; Span<int> values = new(data, 3); int key = 2;
                    if (!SequenceEqual<int>(values.AsImmutable(), values.AsImmutable())) return 1;
                    if (IndexOf<int>(values.AsImmutable(), &key) != 1) return 2;
                    return 0;
                }
                """)).ToArray();
        var (unit, _) = Compiler.Check(files, options: new() { NoStdLib = true });
        Assert.True(files.Select(file => file.Id).ToHashSet().SetEquals(unit.FileUsings.Keys));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void HostWindowOracleChecksFirstRunDirectionVisitCountAndNoMutation(int optimization)
    {
        List<int[]> samples = [[], [4], [4, 4], [4, 4, 0, 4, 4, 4], [4, 0, 4, 0, 4],
            [0, 0, 4, 4], [0, 1, 2, 3, 4], [4, 4, 4, 4]];
        var random = new Random(586221);
        for (int row = 0; row < 24; row++)
            samples.Add(Enumerable.Range(0, random.Next(0, 26)).Select(_ => random.Next(-2, 5)).ToArray());
        var packet = new List<int>();
        int rows = 0;
        foreach (int[] sample in samples)
        foreach (int count in new[] { 0, 1, 2, 3, sample.Length, sample.Length + 1 }.Distinct())
        {
            int key = rows % 5;
            packet.AddRange([sample.Length, count, key, WindowOracle(sample, count, key, ordered: true),
                WindowOracle(sample, count, key, ordered: false)]);
            packet.AddRange(sample);
            rows++;
        }
        Run(Allocator + $$"""
            int calls = 0;
            immutable(int)* origin = (immutable(int)*)1;
            immutable(int)* needle = (immutable(int)*)1;
            bool Match(immutable(int)* element, immutable(int)* key) {
                if (element != origin + calls || key != needle) throw new Exception("borrow/order mismatch");
                calls++; return *element >= *key;
            }
            int main() {
                int[{{packet.Count}}] packet = [{{string.Join(',', packet)}}]; nuint at = 0;
                int baseline = attempts; deny = true;
                for (int row = 0; row < {{rows}}; row++) {
                    nuint length = (nuint)packet[at++]; nuint wanted = (nuint)packet[at++];
                    int key = packet[at++]; nuint expected = (nuint)packet[at++]; nuint defaultExpected = (nuint)packet[at++];
                    int[32] data; int[32] saved;
                    for (nuint i = 0; i < length; i++) { data[i] = packet[at++]; saved[i] = data[i]; }
                    Span<int> values = new(data, length); origin = &data[0]; needle = &key; calls = 0;
                    if (FindRun<int>(values.AsImmutable(), wanted, &key, Match) != expected) return 1;
                    nuint expectedCalls = 0;
                    if (wanted != 0 && wanted <= length) {
                        expectedCalls = length; if (expected < length) expectedCalls = expected + wanted;
                    }
                    if (calls != (int)expectedCalls) return 2;
                    if (FindRun<int>(values.AsImmutable(), wanted, &key) != defaultExpected) return 3;
                    for (nuint i = 0; i < length; i++) if (data[i] != saved[i]) return 4;
                }
                deny = false;
                if (attempts != baseline || allocated != released) return 5;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoAccessPreflightsAndDefaultStringFloatingContractsRemainAllocationFree(int optimization)
        => Run(Allocator + """
            int calls = 0;
            bool Unexpected(immutable(int)* element, immutable(int)* key) {
                calls++; throw new Exception("unexpected callback");
            }
            bool SamePrefix(immutable(String)* element, immutable(String)* key) => element.Data[0] == key.Data[0];
            bool Ieee(immutable(double)* element, immutable(double)* key) => *element == *key;
            int main() {
                {
                    Span<immutable(int)> empty = new((immutable(int)*)1, 0);
                    int[4] storage = [1, 2, 3, 4];
                    Span<immutable(int)> nonempty = new(&storage[0], 4);
                    String[4] data = [s"a", s"a\0b", s"a\0b", s"a"];
                    String key = s"a\0b"; Span<String> text = new(data, 4);
                    double nan = 0.0 / 0.0; double[4] numbers = [1.0, nan, nan, 3.0]; Span<double> floats = new(numbers, 4);
                    double[3] zeroData = [-0.0, 0.0, 1.0]; Span<double> zeros = new(zeroData, 3); double zero = 0.0;
                    int baseline = attempts; deny = true;
                    if (FindRun<int>(empty, 0, (immutable(int)*)1, Unexpected) != 0) return 1;
                    if (FindRun<int>(empty, 1, (immutable(int)*)1, Unexpected) != 0) return 2;
                    if (FindRun<int>(nonempty, 0, (immutable(int)*)1, Unexpected) != 0) return 3;
                    if (FindRun<int>(nonempty, 5, (immutable(int)*)1, Unexpected) != 4) return 4;
                    nuint maximum = (nuint)0 - (nuint)1;
                    if (FindRun<int>(nonempty, maximum, (immutable(int)*)1, Unexpected) != 4) return 5;
                    if (FindRun<int>(nonempty, 0, (immutable(int)*)1) != 0 || FindRun<int>(nonempty, maximum, (immutable(int)*)1) != 4) return 6;
                    if (calls != 0) return 7;
                    if (FindRun<String>(text.AsImmutable(), 2, &key) != 1) return 8;
                    if (FindRun<String>(text.AsImmutable(), 2, &key, SamePrefix) != 0) return 9;
                    if (FindRun<double>(floats.AsImmutable(), 2, &nan) != 1) return 10;
                    if (FindRun<double>(floats.AsImmutable(), 2, &nan, Ieee) != 4) return 11;
                    if (FindRun<double>(zeros.AsImmutable(), 2, &zero) != 0) return 12;
                    deny = false; if (attempts != baseline) return 13;
                }
                if (allocated != released) return 14;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableOwnersStayBorrowedAndOriginalCallbackExceptionsPropagate(int optimization)
        => Run(Allocator + """
            int made = 0; int dead = 0; int calls = 0; int failAt = -1;
            immutable(Owner)* origin = (immutable(Owner)*)1;
            immutable(Owner)* needle = (immutable(Owner)*)1;
            struct Owner {
                public int Id; public String Token;
                public Owner(int id) { Id = id; Token = s"owner"; made++; }
                ~Owner() { dead++; }
            }
            bool Match(immutable(Owner)* element, immutable(Owner)* key) {
                if (element != origin + calls || key != needle) throw new Exception("not borrowed", 99);
                int position = calls; calls++;
                if (position == failAt) throw new Exception("original predicate", 77);
                return element.Id == key.Id;
            }
            int main() {
                {
                    Vector<Owner> owners = new(); owners.Reserve(6); int[6] ids = [7, 7, 0, 7, 7, 7];
                    for (nuint i = 0; i < 6; i++) owners.Add(new Owner(ids[i]));
                    origin = &owners[0]; needle = &owners[4]; nuint[6] tokens;
                    for (nuint i = 0; i < 6; i++) tokens[i] = (nuint)owners[i].Token.Data;
                    nuint capacity = owners.Capacity; int baseline = attempts; deny = true;
                    if (FindRun<Owner>(owners.AsImmutable(), 2, needle, Match) != 0 || calls != 2) return 1;
                    calls = 0;
                    if (FindRun<Owner>(owners.AsImmutable(), 3, needle, Match) != 3 || calls != 6) return 2;
                    calls = 0; needle = &owners[2];
                    if (FindRun<Owner>(owners.AsImmutable(), 2, needle, Match) != 6 || calls != 6) return 3;
                    deny = false; if (attempts != baseline) return 4;
                    needle = &owners[4];
                    for (int failure = 0; failure < 6; failure++) {
                        calls = 0; failAt = failure; nuint result = 99; bool caught = false;
                        try { result = FindRun<Owner>(owners.AsImmutable(), 3, needle, Match); }
                        catch (ref Exception error) {
                            caught = error.GetCode() == 77 && CString::Equals(error.GetMessage(), "original predicate");
                        }
                        if (!caught || result != 99 || calls != failure + 1) return 5;
                        if (made != 6 || dead != 0 || owners.Count != 6 || owners.Capacity != capacity) return 6;
                        for (nuint i = 0; i < 6; i++)
                            if (&owners[i] != origin + i || owners[i].Id != ids[i] || (nuint)owners[i].Token.Data != tokens[i] || owners[i].Token.Length != 5) return 7;
                    }
                }
                if (made != 6 || dead != 6 || allocated != released) return 8;
                return 0;
            }
            """, optimization);

    private static int WindowOracle(int[] values, int count, int key, bool ordered)
    {
        if (count == 0) return 0;
        if (count > values.Length) return values.Length;
        return Enumerable.Range(0, values.Length - count + 1)
            .Where(start => values.Skip(start).Take(count).All(value => ordered ? value >= key : value == key))
            .DefaultIfEmpty(values.Length).First();
    }

    private static void Run(string program, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("find-run.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"FindRun exit {result.ExitCode}: {result.StandardError}");
    }
}
