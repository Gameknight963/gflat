namespace gflat.Tests.Std.Collections;

public sealed class SpanIncludesTests
{
    private const string Allocator = """
        using std; using std::Text; using std::Memory; using std::Collections;
        extern void*? malloc(nuint bytes); extern void free(void* storage);
        int attempts = 0; int allocations = 0; int releases = 0; bool deny = false;
        namespace Memory {
            public replace void*? Allocate(nuint bytes) {
                attempts++; if (deny) return null;
                void*? result = malloc(bytes); if (result != null) allocations++; return result;
            }
            public replace void Free(void* storage) nothrow { releases++; free(storage); }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IndependentMultisetCountOracleCoversBothOrdersDuplicatesGapsAndDefaultComparison(int optimization)
    {
        List<(int[] Values, int[] Required)> cases = [([], []), ([1], []), ([], [1]), ([1], [1, 1]),
            ([17, 17, 42], [17, 17]), ([17, 17, 42], [17, 42]), ([17, 17, 42], [17, 17, 17]),
            ([1, 3, 5], [2]), ([1, 3, 5], [7]), ([1, 3, 5], [0]), ([1, 2, 2, 4, 6], [2, 2, 6]),
            ([int.MinValue, 0, int.MaxValue], [int.MinValue, int.MaxValue])];
        var random = new Random(634875);
        for (int row = 0; row < 24; row++)
        {
            int[] values = Enumerable.Range(0, random.Next(0, 21)).Select(_ => random.Next(0, 6)).ToArray();
            int[] required = row % 2 == 0 && values.Length != 0
                ? Enumerable.Range(0, random.Next(0, values.Length + 1)).Select(_ => values[random.Next(values.Length)]).ToArray()
                : Enumerable.Range(0, random.Next(0, 13)).Select(_ => random.Next(0, 7)).ToArray();
            cases.Add((values, required));
        }
        var packet = new List<int>();
        foreach (var row in cases)
        {
            var available = row.Values.GroupBy(value => value).ToDictionary(group => group.Key, group => group.Count());
            bool expected = row.Required.GroupBy(value => value)
                .All(group => available.TryGetValue(group.Key, out int count) && count >= group.Count());
            packet.AddRange([row.Values.Length, row.Required.Length, expected ? 1 : 0]);
            packet.AddRange(row.Values.Order()); packet.AddRange(row.Required.Order());
        }
        string literals = string.Join(',', packet.Select(value => value == int.MinValue ? "(-2147483647 - 1)" : value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Run(Allocator + $$"""
            int Ascending(immutable(int)* left, immutable(int)* right) {
                if (*left < *right) return -1; if (*left > *right) return 1; return 0;
            }
            int Descending(immutable(int)* left, immutable(int)* right) {
                if (*left > *right) return -1; if (*left < *right) return 1; return 0;
            }
            int main() {
                int[{{packet.Count}}] packet = [{{literals}}]; nuint at = 0; int before = attempts; deny = true;
                for (int row = 0; row < {{cases.Count}}; row++) {
                    nuint length = (nuint)packet[at++]; nuint count = (nuint)packet[at++]; bool expected = packet[at++] != 0;
                    int[32] data; int[32] reversed; int[32] required; int[32] reversedRequired; int[24] saved; int[24] savedRequired;
                    data[0] = 91; reversed[0] = 91; data[length + 1] = 92; reversed[length + 1] = 92;
                    required[0] = 71; reversedRequired[0] = 71; required[1] = 72; reversedRequired[1] = 72;
                    required[count + 2] = 73; reversedRequired[count + 2] = 73;
                    for (nuint i = 0; i < length; i++) { int value = packet[at++]; data[i + 1] = value; reversed[length - i] = value; saved[i] = value; }
                    for (nuint i = 0; i < count; i++) { int value = packet[at++]; required[i + 2] = value; reversedRequired[count + 1 - i] = value; savedRequired[i] = value; }
                    Span<int> all = new(data, length + 2); Span<int> descending = new(reversed, length + 2);
                    Span<int> wanted = new(required, count + 3); Span<int> wantedDescending = new(reversedRequired, count + 3);
                    if (Includes<int>(all.Slice(1, length).AsImmutable(), wanted.Slice(2, count).AsImmutable(), Ascending) != expected) return 1;
                    if (Includes<int>(all.Slice(1, length).AsImmutable(), wanted.Slice(2, count).AsImmutable()) != expected) return 2;
                    if (Includes<int>(descending.Slice(1, length).AsImmutable(), wantedDescending.Slice(2, count).AsImmutable(), Descending) != expected) return 3;
                    for (nuint i = 0; i < length; i++) if (data[i + 1] != saved[i] || reversed[length - i] != saved[i]) return 4;
                    for (nuint i = 0; i < count; i++) if (required[i + 2] != savedRequired[i] || reversedRequired[count + 1 - i] != savedRequired[i]) return 5;
                    if (data[0] != 91 || reversed[0] != 91 || data[length + 1] != 92 || reversed[length + 1] != 92) return 6;
                    if (required[0] != 71 || reversedRequired[0] != 71 || required[1] != 72 || reversedRequired[1] != 72 || required[count + 2] != 73 || reversedRequired[count + 2] != 73) return 7;
                }
                deny = false;
                if (attempts != before || allocations != releases) return 8;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExactValuesFirstCallbackTraceEarlyFailurePreflightsAndAliasedSpansArePreserved(int optimization)
        => Run(Allocator + """
            int calls = 0; int limit = 5; int[5] rights = [0, 0, 1, 2, 2];
            immutable(int)* origin = (immutable(int)*)1; immutable(int)* target = (immutable(int)*)1;
            int Unexpected(immutable(int)* left, immutable(int)* right) { calls++; throw new Exception("unexpected comparison", 99); }
            int Compare(immutable(int)* left, immutable(int)* right) {
                if (calls >= limit || left != origin + calls || right != target + rights[calls]) throw new Exception("comparison order", 98);
                calls++; if (*left < *right) return -7; if (*left > *right) return 9; return 0;
            }
            int Plain(immutable(int)* left, immutable(int)* right) { if (*left < *right) return -1; if (*left > *right) return 1; return 0; }
            int main() {
                int[5] data = [1, 2, 2, 4, 6]; int[5] saved = [1, 2, 2, 4, 6]; int[3] need = [2, 2, 6]; int[2] gap = [2, 3];
                Span<int> all = new(data, 5); Span<int> wanted = new(need, 3); Span<int> absent = new(gap, 2);
                Span<immutable(int)> empty = new();
                int before = attempts; deny = true;
                if (!Includes<int>(empty, empty, Unexpected) || !Includes<int>(all.AsImmutable(), empty, Unexpected)) return 1;
                if (Includes<int>(empty, wanted.AsImmutable(), Unexpected) || Includes<int>(all.Slice(0, 2).AsImmutable(), wanted.AsImmutable(), Unexpected) || calls != 0) return 2;
                if (!Includes<int>(empty, empty) || Includes<int>(empty, wanted.AsImmutable())) return 3;
                origin = &data[0]; target = &need[0];
                if (!Includes<int>(all.AsImmutable(), wanted.AsImmutable(), Compare) || calls != 5) return 4;
                calls = 0; limit = 4; rights[3] = 1; target = &gap[0];
                if (Includes<int>(all.AsImmutable(), absent.AsImmutable(), Compare) || calls != 4) return 5;
                if (!Includes<int>(all.AsImmutable(), all.AsImmutable(), Plain) || !Includes<int>(all.AsImmutable(), all.Slice(1, 3).AsImmutable())) return 6;
                if (Includes<int>(all.Slice(0, 3).AsImmutable(), all.Slice(1, 3).AsImmutable(), Plain)) return 7;
                for (nuint i = 0; i < 5; i++) if (data[i] != saved[i]) return 8;
                if (need[0] != 2 || need[1] != 2 || need[2] != 6 || gap[0] != 2 || gap[1] != 3) return 9;
                deny = false;
                if (attempts != before || allocations != releases) return 10;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableInputsAndOriginalExceptionPreserveOwnersAddressesAndCallerResult(int optimization)
        => Run(Allocator + """
            int made = 0; int dead = 0; int calls = 0; int failAt = -1; int[5] rights = [0, 0, 1, 2, 2];
            struct Owner {
                public int Id; public String Token;
                public Owner(int id) { Id = id; Token = s"owner"; made++; }
                ~Owner() { dead++; }
            }
            immutable(Owner)* origin = (immutable(Owner)*)1; immutable(Owner)* target = (immutable(Owner)*)1;
            int Compare(immutable(Owner)* left, immutable(Owner)* right) {
                if (calls >= 5 || left != origin + calls || right != target + rights[calls]) throw new Exception("owner address", 99);
                int position = calls; calls++; if (position == failAt) throw new Exception("original comparison", 73);
                if (left.Id < right.Id) return -1; if (left.Id > right.Id) return 1; return 0;
            }
            int main() {
                {
                    Vector<Owner> values = new(); Vector<Owner> required = new(); values.Reserve(5); required.Reserve(3);
                    int[5] ids = [1, 2, 2, 4, 6]; int[3] wanted = [2, 2, 6]; nuint[5] tokens; nuint[3] requiredTokens;
                    for (nuint i = 0; i < 5; i++) values.Add(new Owner(ids[i]));
                    for (nuint i = 0; i < 3; i++) required.Add(new Owner(wanted[i]));
                    origin = &values[0]; target = &required[0]; nuint capacity = values.Capacity; nuint requiredCapacity = required.Capacity;
                    for (nuint i = 0; i < 5; i++) tokens[i] = (nuint)values[i].Token.Data;
                    for (nuint i = 0; i < 3; i++) requiredTokens[i] = (nuint)required[i].Token.Data;
                    int before = attempts; deny = true;
                    if (!Includes<Owner>(values.AsImmutable(), required.AsImmutable(), Compare) || calls != 5) return 1;
                    deny = false; if (attempts != before) return 2;
                    for (int failure = 0; failure < 5; failure++) {
                        calls = 0; failAt = failure; bool result = false; bool caught = false;
                        try { result = Includes<Owner>(values.AsImmutable(), required.AsImmutable(), Compare); }
                        catch (ref Exception error) { caught = error.GetCode() == 73 && CString::Equals(error.GetMessage(), "original comparison"); }
                        if (!caught || result || calls != failure + 1 || made != 8 || dead != 0) return 3;
                        if (values.Count != 5 || required.Count != 3 || values.Capacity != capacity || required.Capacity != requiredCapacity) return 4;
                        for (nuint i = 0; i < 5; i++) if (&values[i] != origin + i || values[i].Id != ids[i] || (nuint)values[i].Token.Data != tokens[i] || values[i].Token.Length != 5) return 5;
                        for (nuint i = 0; i < 3; i++) if (&required[i] != target + i || required[i].Id != wanted[i] || (nuint)required[i].Token.Data != requiredTokens[i] || required[i].Token.Length != 5) return 6;
                    }
                }
                if (made != 8 || dead != 8 || allocations != releases) return 7;
                return 0;
            }
            """, optimization);

    private static void Run(string program, int optimization)
    {
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("span-includes.gf", program));
        string ir = Compiler.Emit(files, options: new() { NoStdLib = true, InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
