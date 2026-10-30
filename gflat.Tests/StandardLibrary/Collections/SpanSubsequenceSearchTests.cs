namespace gflat.Tests.Std.Collections;

public sealed class SpanSubsequenceSearchTests
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
    public void ExistingEqualitySubsetChecksWithoutTheUnusedSubsequenceSource()
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
    public void HostSpanOracleChecksFirstMatchAndInputsWithoutLibraryAllocation(int optimization)
    {
        List<(int[] Values, int[] Pattern)> cases = [([], []), ([1], []), ([], [1]),
            ([1, 2], [1, 2, 3]), ([1, 2, 3], [1, 2, 3]), ([1, 2, 3], [2, 3]),
            ([1, 1, 1, 2], [1, 1, 2]), ([1, 2, 1, 2, 3], [1, 2, 3]),
            ([1, 2, 1, 2], [1, 2]), ([1, 2, 3], [2, 4]), ([4, 4, 4], [4])];
        var random = new Random(586222);
        for (int row = 0; row < 24; row++)
        {
            int[] values = Enumerable.Range(0, random.Next(0, 21)).Select(_ => random.Next(0, 5)).ToArray();
            int[] pattern;
            if (row % 2 == 0 && values.Length != 0)
            {
                int start = random.Next(values.Length);
                pattern = values.Skip(start).Take(random.Next(0, values.Length - start + 1)).ToArray();
            }
            else pattern = Enumerable.Range(0, random.Next(0, 10)).Select(_ => random.Next(0, 5)).ToArray();
            cases.Add((values, pattern));
        }
        var packet = new List<int>();
        foreach (var row in cases)
        {
            int expected = ((ReadOnlySpan<int>)row.Values).IndexOf((ReadOnlySpan<int>)row.Pattern);
            if (expected < 0) expected = row.Values.Length;
            packet.AddRange([row.Values.Length, row.Pattern.Length, expected]);
            packet.AddRange(row.Values); packet.AddRange(row.Pattern);
        }
        Run(Allocator + $$"""
            bool EqualInt(immutable(int)* element, immutable(int)* pattern) => *element == *pattern;
            int main() {
                int[{{packet.Count}}] packet = [{{string.Join(',', packet)}}]; nuint at = 0;
                int baseline = attempts; deny = true;
                for (int row = 0; row < {{cases.Count}}; row++) {
                    nuint length = (nuint)packet[at++]; nuint patternLength = (nuint)packet[at++]; nuint expected = (nuint)packet[at++];
                    int[24] data; int[24] saved; int[20] sought; int[20] patternSaved;
                    for (nuint i = 0; i < length; i++) { data[i] = packet[at++]; saved[i] = data[i]; }
                    for (nuint i = 0; i < patternLength; i++) { sought[i] = packet[at++]; patternSaved[i] = sought[i]; }
                    Span<int> values = new(data, length); Span<int> pattern = new(sought, patternLength);
                    if (IndexOf<int>(values.AsImmutable(), pattern.AsImmutable(), EqualInt) != expected) return 1;
                    if (IndexOf<int>(values.AsImmutable(), pattern.AsImmutable()) != expected) return 2;
                    for (nuint i = 0; i < length; i++) if (data[i] != saved[i]) return 3;
                    for (nuint i = 0; i < patternLength; i++) if (sought[i] != patternSaved[i]) return 4;
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
    public void ValidPreflightsAsymmetricTraceOverlapAndDefaultsKeepTheirContracts(int optimization)
        => Run(Allocator + """
            int calls = 0;
            immutable(int)* origin = ((nuint)1).reinterpret<immutable(int)*>();
            immutable(int)* sought = ((nuint)1).reinterpret<immutable(int)*>();
            int[5] lefts = [0, 1, 1, 2, 3]; int[5] rights = [0, 1, 0, 0, 1];
            bool Unexpected(immutable(int)* element, immutable(int)* pattern) {
                calls++; throw new Exception("unexpected callback");
            }
            bool Asymmetric(immutable(int)* element, immutable(int)* pattern) {
                if (calls >= 5 || element != origin + lefts[calls] || pattern != sought + rights[calls])
                    throw new Exception("comparison order");
                calls++; return *element >= *pattern;
            }
            bool SamePrefix(immutable(String)* element, immutable(String)* pattern) => element.Data[0] == pattern.Data[0];
            bool Ieee(immutable(double)* element, immutable(double)* pattern) => *element == *pattern;
            int main() {
                {
                    int[4] data = [3, 1, 4, 4]; int[2] small = [2, 3]; int[5] overlap = [1, 2, 1, 2, 3];
                    Span<int> values = new(data, 4); Span<int> pattern = new(small, 2); Span<int> shared = new(overlap, 5);
                    Span<immutable(int)> empty = new(((nuint)1).reinterpret<immutable(int)*>(), 0);
                    String[4] textData = [s"a", s"a\0b", s"a\0b", s"z"]; Span<String> text = new(textData, 4);
                    double nan = 0.0 / 0.0; double[4] numbers = [1.0, nan, nan, 2.0]; double[2] nanPattern = [nan, nan];
                    Span<double> floats = new(numbers, 4); Span<double> nans = new(nanPattern, 2);
                    double[3] zeroData = [-0.0, 0.0, 1.0]; double[2] zeroPattern = [0.0, -0.0];
                    Span<double> zeros = new(zeroData, 3); Span<double> zeroView = new(zeroPattern, 2);
                    int baseline = attempts; deny = true;
                    if (IndexOf<int>(empty, empty, Unexpected) != 0) return 1;
                    if (IndexOf<int>(empty, pattern.AsImmutable(), Unexpected) != 0) return 2;
                    if (IndexOf<int>(values.AsImmutable(), empty, Unexpected) != 0) return 3;
                    if (IndexOf<int>(values.AsImmutable(), shared.AsImmutable(), Unexpected) != 4 || calls != 0) return 4;
                    if (IndexOf<int>(empty, pattern.AsImmutable()) != 0 || IndexOf<int>(values.AsImmutable(), empty) != 0) return 5;
                    origin = &data[0]; sought = &small[0];
                    if (IndexOf<int>(values.AsImmutable(), pattern.AsImmutable(), Asymmetric) != 2 || calls != 5) return 6;
                    if (IndexOf<int>(shared.AsImmutable(), shared.Slice(2, 3).AsImmutable()) != 2) return 7;
                    if (IndexOf<int>(shared.AsImmutable(), shared.AsImmutable()) != 0) return 8;
                    if (IndexOf<String>(text.AsImmutable(), text.Slice(1, 2).AsImmutable()) != 1) return 9;
                    if (IndexOf<String>(text.AsImmutable(), text.Slice(1, 2).AsImmutable(), SamePrefix) != 0) return 10;
                    if (IndexOf<double>(floats.AsImmutable(), nans.AsImmutable()) != 1) return 11;
                    if (IndexOf<double>(floats.AsImmutable(), nans.AsImmutable(), Ieee) != 4) return 12;
                    if (IndexOf<double>(zeros.AsImmutable(), zeroView.AsImmutable()) != 0) return 13;
                    deny = false; if (attempts != baseline) return 14;
                }
                if (allocated != released) return 15;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OverlappingNoncopyableOwnersAreBorrowedAndOriginalExceptionsPropagate(int optimization)
        => Run(Allocator + """
            int made = 0; int dead = 0; int calls = 0; int failAt = -1;
            immutable(Owner)* origin = ((nuint)1).reinterpret<immutable(Owner)*>();
            immutable(Owner)* sought = ((nuint)1).reinterpret<immutable(Owner)*>();
            int[7] lefts = [0, 1, 2, 1, 2, 3, 4]; int[7] rights = [0, 1, 2, 0, 0, 1, 2];
            struct Owner {
                public int Id; public String Token;
                public Owner(int id) { Id = id; Token = s"owner"; made++; }
                ~Owner() { dead++; }
            }
            bool Match(immutable(Owner)* element, immutable(Owner)* pattern) {
                if (calls >= 7 || element != origin + lefts[calls] || pattern != sought + rights[calls])
                    throw new Exception("not borrowed", 99);
                int position = calls; calls++;
                if (position == failAt) throw new Exception("original predicate", 77);
                return element.Id == pattern.Id;
            }
            int main() {
                {
                    Vector<Owner> owners = new(); owners.Reserve(6); int[6] ids = [1, 2, 1, 2, 3, 4];
                    for (nuint i = 0; i < 6; i++) owners.Add(new Owner(ids[i]));
                    origin = &owners[0]; sought = &owners[2]; nuint[6] tokens;
                    for (nuint i = 0; i < 6; i++) tokens[i] = (owners[i].Token.Data).reinterpret<nuint>();
                    nuint capacity = owners.Capacity; int baseline = attempts; deny = true;
                    if (IndexOf<Owner>(owners.AsImmutable(), owners.AsImmutable().Slice(2, 3), Match) != 2 || calls != 7) return 1;
                    deny = false; if (attempts != baseline) return 2;
                    for (int failure = 0; failure < 7; failure++) {
                        calls = 0; failAt = failure; nuint result = 99; bool caught = false;
                        try { result = IndexOf<Owner>(owners.AsImmutable(), owners.AsImmutable().Slice(2, 3), Match); }
                        catch (ref Exception error) {
                            caught = error.GetCode() == 77 && CString::Equals(error.GetMessage(), "original predicate");
                        }
                        if (!caught || result != 99 || calls != failure + 1) return 3;
                        if (made != 6 || dead != 0 || owners.Count != 6 || owners.Capacity != capacity) return 4;
                        for (nuint i = 0; i < 6; i++)
                            if (&owners[i] != origin + i || owners[i].Id != ids[i] || (owners[i].Token.Data).reinterpret<nuint>() != tokens[i] || owners[i].Token.Length != 5) return 5;
                    }
                }
                if (made != 6 || dead != 6 || allocated != released) return 6;
                return 0;
            }
            """, optimization);

    private static void Run(string program, int optimization)
    {
        var sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("span-subsequence-search.gf", program));
        string ir = Compiler.Emit(sources, options: new() { InternalizeFunctions = true });
        var result = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
        Assert.True(result.ExitCode == 0, $"Span subsequence search exit {result.ExitCode}: {result.StandardError}");
    }
}
