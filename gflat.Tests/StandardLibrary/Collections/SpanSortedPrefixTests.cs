namespace gflat.Tests.Std.Collections;

public sealed class SpanSortedPrefixTests
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
    public void IndependentAscendingAndDescendingOraclesCoverDuplicatesExtremesAndSubspans(int optimization)
    {
        List<int[]> cases = [[], [1], [1, 1], [1, 3, 2, 4], [3, 2, 1], [1, 2, 3],
            [int.MinValue, 0, int.MaxValue], [int.MaxValue, int.MinValue], [3, 3, 2, 2, 1],
            Enumerable.Range(0, 24).ToArray(), Enumerable.Range(0, 24).Reverse().ToArray(),
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, -1]];
        var random = new Random(794215);
        for (int row = 0; row < 24; row++)
            cases.Add(Enumerable.Range(0, random.Next(0, 25)).Select(_ => random.Next(-8, 9)).ToArray());
        var packet = new List<int>();
        foreach (int[] values in cases)
        {
            var pairs = values.Zip(values.Skip(1));
            int ascending = pairs.TakeWhile(pair => pair.First.CompareTo(pair.Second) <= 0).Count();
            int descending = pairs.TakeWhile(pair => pair.Second.CompareTo(pair.First) <= 0).Count();
            ascending = ascending == Math.Max(0, values.Length - 1) ? values.Length : ascending + 1;
            descending = descending == Math.Max(0, values.Length - 1) ? values.Length : descending + 1;
            packet.AddRange([values.Length, ascending, descending]); packet.AddRange(values);
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
                int[{{packet.Count}}] packet = [{{literals}}]; nuint at = 0;
                int before = attempts; deny = true;
                for (int row = 0; row < {{cases.Count}}; row++) {
                    nuint length = (nuint)packet[at++]; nuint ascending = (nuint)packet[at++]; nuint descending = (nuint)packet[at++];
                    int[32] data; int[32] saved; data[0] = 91; data[length + 1] = 92;
                    for (nuint i = 0; i < length; i++) { data[i + 1] = packet[at++]; saved[i] = data[i + 1]; }
                    Span<int> all = new(data, length + 2); Span<immutable(int)> values = all.Slice(1, length).AsImmutable();
                    if (FindUnsortedIndex<int>(values, Ascending) != ascending || FindUnsortedIndex<int>(values) != ascending) return 1;
                    if (FindUnsortedIndex<int>(values, Descending) != descending) return 2;
                    if (IsSorted<int>(values, Ascending) != (ascending == length) || IsSorted<int>(values) != (ascending == length)) return 3;
                    if (IsSorted<int>(values, Descending) != (descending == length)) return 4;
                    if (data[0] != 91 || data[length + 1] != 92) return 5;
                    for (nuint i = 0; i < length; i++) if (data[i + 1] != saved[i]) return 6;
                }
                deny = false;
                if (attempts != before || allocations != releases) return 7;
                return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AdjacentAddressesDirectionAndEarlyStopAreExactAndShortSpansNeverCompare(int optimization)
        => Run(Allocator + """
            int calls = 0; immutable(int)* origin = (immutable(int)*)1;
            int Unexpected(immutable(int)* left, immutable(int)* right) {
                calls++; throw new Exception("unexpected comparison", 99);
            }
            int OrderedTrace(immutable(int)* left, immutable(int)* right) {
                if (calls >= 3 || left != origin + calls || right != origin + calls + 1)
                    throw new Exception("wrong comparison address", 98);
                calls++;
                if (*left < *right) return -7; if (*left > *right) return 9; return 0;
            }
            int main() {
                int[7] data = [91, 1, 2, 3, 1, 9, 92]; int[7] saved = [91, 1, 2, 3, 1, 9, 92];
                Span<int> all = new(data, 7); Span<immutable(int)> values = all.Slice(1, 5).AsImmutable();
                Span<immutable(int)> empty = new((immutable(int)*)1, 0);
                Span<immutable(int)> singleton = all.Slice(1, 1).AsImmutable();
                int before = attempts; deny = true;
                if (FindUnsortedIndex<int>(empty, Unexpected) != 0 || FindUnsortedIndex<int>(singleton, Unexpected) != 1 || calls != 0) return 1;
                if (FindUnsortedIndex<int>(empty) != 0 || FindUnsortedIndex<int>(singleton) != 1) return 2;
                origin = &data[1];
                if (FindUnsortedIndex<int>(values, OrderedTrace) != 3 || calls != 3) return 3;
                for (nuint i = 0; i < 7; i++) if (data[i] != saved[i]) return 4;
                deny = false;
                if (attempts != before || allocations != releases) return 5;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableOwnersAreBorrowedAndComparatorExceptionsPreserveInputAndResult(int optimization)
        => Run(Allocator + """
            int made = 0; int dead = 0; int calls = 0; int failAt = -1;
            struct Owner {
                public int Id; public String Token;
                public Owner(int id) { Id = id; Token = s"owner"; made++; }
                ~Owner() { dead++; }
            }
            immutable(Owner)* origin = (immutable(Owner)*)1;
            int Compare(immutable(Owner)* left, immutable(Owner)* right) {
                if (calls >= 3 || left != origin + calls || right != origin + calls + 1)
                    throw new Exception("wrong owner address", 99);
                int position = calls; calls++;
                if (position == failAt) throw new Exception("original comparison", 73);
                if (left.Id < right.Id) return -1; if (left.Id > right.Id) return 1; return 0;
            }
            int main() {
                {
                    Vector<Owner> owners = new(); owners.Reserve(6); int[6] ids = [1, 2, 3, 2, 4, 5]; nuint[6] tokens;
                    for (nuint i = 0; i < 6; i++) owners.Add(new Owner(ids[i]));
                    origin = &owners[0]; nuint capacity = owners.Capacity;
                    for (nuint i = 0; i < 6; i++) tokens[i] = (nuint)owners[i].Token.Data;
                    int before = attempts; deny = true;
                    if (FindUnsortedIndex<Owner>(owners.AsImmutable(), Compare) != 3 || calls != 3) return 1;
                    deny = false; if (attempts != before) return 2;
                    for (int failure = 0; failure < 3; failure++) {
                        calls = 0; failAt = failure; nuint result = 42; bool caught = false;
                        try { result = FindUnsortedIndex<Owner>(owners.AsImmutable(), Compare); }
                        catch (ref Exception error) {
                            caught = error.GetCode() == 73 && CString::Equals(error.GetMessage(), "original comparison");
                        }
                        if (!caught || result != 42 || calls != failure + 1 || made != 6 || dead != 0) return 3;
                        if (owners.Count != 6 || owners.Capacity != capacity) return 4;
                        for (nuint i = 0; i < 6; i++)
                            if (&owners[i] != origin + i || owners[i].Id != ids[i] || (nuint)owners[i].Token.Data != tokens[i] || owners[i].Token.Length != 5) return 5;
                    }
                }
                if (made != 6 || dead != 6 || allocations != releases) return 6;
                return 0;
            }
            """, optimization);

    private static void Run(string program, int optimization)
    {
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Std"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Append(new SourceFile("span-sorted-prefix.gf", program));
        string ir = Compiler.Emit(files, options: new() { NoStdLib = true, InternalizeFunctions = true });
        _ = CompilerTestHelper.RunIr(ir, optimization, expectedExitCode: 0);
    }
}
