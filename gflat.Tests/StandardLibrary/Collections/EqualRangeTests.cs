using gflat.CompileExceptions;

namespace gflat.Tests.Std.Collections;

public sealed class EqualRangeTests
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
    public void SortedDuplicatesMatchHostRangesAndDefaultWrapperWithLogarithmicCalls(int optimization)
    {
        List<int[]> inputs = [[], [2], [2, 2, 2], [1, 2, 2, 2, 4], [int.MinValue, 0, int.MaxValue]];
        var random = new Random(163751);
        for (int row = 0; row < 12; row++)
            inputs.Add(Enumerable.Range(0, random.Next(0, 41)).Select(_ => random.Next(-3, 7)).Order().ToArray());
        int[] keys = [int.MinValue, -4, 0, 2, 3, 6, 7, int.MaxValue];
        var packet = new List<int>();
        foreach (var input in inputs)
            foreach (int key in keys)
            {
                int start = Array.IndexOf(input, key);
                int limit = input.Length == 0 ? 0 : 2 * (System.Numerics.BitOperations.Log2((uint)input.Length) + 1);
                packet.AddRange([input.Length, key, Math.Max(start, 0), input.Count(value => value == key), limit]);
                packet.AddRange(input);
            }
        Run(Allocator + $$"""
            int calls = 0; immutable(int)* wantedKey = ((nuint)1).reinterpret<immutable(int)*>();
            int Compare(immutable(int)* left, immutable(int)* right) {
                if (right != wantedKey) throw new Exception("key was not borrowed");
                calls++; if (*left < *right) return -1; if (*left > *right) return 1; return 0;
            }
            int main() {
                int[{{packet.Count}}] packet = [{{string.Join(',', packet)}}]; nuint at = 0;
                int baseline = attempts; deny = true;
                for (int row = 0; row < {{inputs.Count * keys.Length}}; row++) {
                    nuint count = (nuint)packet[at++]; int key = packet[at++]; nuint start = (nuint)packet[at++];
                    nuint matches = (nuint)packet[at++]; int limit = packet[at++]; int[40] data; int[40] saved;
                    for (nuint i = 0; i < count; i++) { data[i] = packet[at++]; saved[i] = data[i]; }
                    Span<int> values = new(data, count); wantedKey = &key; calls = 0;
                    var explicitRange = EqualRange<int>(values.AsImmutable(), &key, Compare);
                    var defaultRange = EqualRange<int>(values.AsImmutable(), &key);
                    if (explicitRange.Length != matches || defaultRange.Length != matches || calls > limit) return 1;
                    if (matches == 0) { if (explicitRange.Data != null || defaultRange.Data != null) return 2; }
                    else if (explicitRange.Data != &data[start] || defaultRange.Data != &data[start]) return 3;
                    for (nuint i = 0; i < count; i++) if (data[i] != saved[i]) return 4;
                }
                if (at != {{packet.Count}} || attempts != baseline || allocated != released) return 5;
                deny = false; return 0;
            }
            """, optimization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DescendingAndCustomEquivalenceBorrowWholeRunsWithoutLinearScanning(int optimization)
        => Run(Allocator + """
            int calls = 0;
            int Descending(immutable(int)* left, immutable(int)* right) {
                calls++; if (*left > *right) return -1; if (*left < *right) return 1; return 0;
            }
            int Decade(immutable(int)* left, immutable(int)* right) {
                calls++; if (*left / 10 < *right / 10) return -1; if (*left / 10 > *right / 10) return 1; return 0;
            }
            int main() {
                int baseline = attempts; deny = true;
                int[5] reverse = [9, 7, 7, 4, 1]; Span<int> descending = new(reverse, 5); int key = 7;
                var found = EqualRange<int>(descending.AsImmutable(), &key, Descending);
                if (found.Length != 2 || found.Data != &reverse[1] || calls > 6) return 1;
                key = 5; found = EqualRange<int>(descending.AsImmutable(), &key, Descending);
                if (!found.IsEmpty || found.Data != null) return 2;
                int[7] grouped = [10, 11, 19, 20, 21, 29, 30]; Span<int> decades = new(grouped, 7); key = 18;
                found = EqualRange<int>(decades.AsImmutable(), &key, Decade);
                if (found.Length != 3 || found.Data != &grouped[0] || found[1] != 11 || found[2] != 19) return 3;
                int[129] same; for (nuint i = 0; i < 129; i++) same[i] = 10 + (int)(i % 10);
                Span<int> equivalent = new(same, 129); key = 17; calls = 0;
                found = EqualRange<int>(equivalent.AsImmutable(), &key, Decade);
                if (found.Length != 129 || found.Data != &same[0] || calls > 16) return 4;
                if (attempts != baseline || allocated != released) return 5; deny = false; return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EmptyViewsCanonicalizeWithoutReadingStorageOrKey(int optimization)
        => Run(Allocator + """
            int Unexpected(immutable(int)* left, immutable(int)* right) { throw new Exception("empty comparison"); }
            int main() {
                Span<immutable(int)> empty = new(); Span<immutable(int)> unreadable = new(((nuint)1).reinterpret<immutable(int)*>(), 0);
                immutable(int)* key = ((nuint)1).reinterpret<immutable(int)*>(); int baseline = attempts; deny = true;
                var first = EqualRange<int>(empty, key, Unexpected); var second = EqualRange<int>(unreadable, key, Unexpected);
                var third = EqualRange<int>(unreadable, key);
                if (first.Length != 0 || second.Length != 0 || third.Length != 0 || first.Data != null || second.Data != null || third.Data != null) return 1;
                if (attempts != baseline) return 2; deny = false; return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableOwnersAndAliasedKeysAreBorrowedAndEveryComparisonFailurePreservesOutput(int optimization)
        => Run(Allocator + """
            int made = 0; int dead = 0; int calls = 0; int failAt = -1;
            immutable(Owner)* origin = ((nuint)1).reinterpret<immutable(Owner)*>(); immutable(Owner)* wantedKey = ((nuint)1).reinterpret<immutable(Owner)*>();
            struct Owner : IComparable<Owner> {
                public int Id; public int Key; public String Token;
                public Owner(int id, int key) { Id = id; Key = key; Token = s"owner"; made++; }
                public immutable int CompareTo(immutable(Owner)* other) {
                    if (Id < 0 || Id >= 7 || Token.Data != origin[Id].Token.Data || other != wantedKey) throw new Exception("not borrowed", 99);
                    int index = calls; calls++; if (index == failAt) throw new Exception("original comparison", 77);
                    if (Key < other.Key) return -1; if (Key > other.Key) return 1; return 0;
                }
                ~Owner() { dead++; }
            }
            int Compare(immutable(Owner)* left, immutable(Owner)* right) {
                if (left != origin + left.Id) throw new Exception("element was not borrowed", 99);
                return left.CompareTo(right);
            }
            int main() {
                {
                    Vector<Owner> owners = new(); owners.Reserve(7); int[7] keys = [1, 2, 2, 2, 4, 7, 9];
                    for (int id = 0; id < 7; id++) owners.Add(new Owner(id, keys[(nuint)id]));
                    Owner key = new(99, 2); origin = &owners[0]; nuint[7] tokens;
                    for (nuint i = 0; i < 7; i++) tokens[i] = (owners[i].Token.Data).reinterpret<nuint>();
                    nuint keyToken = (key.Token.Data).reinterpret<nuint>(); nuint capacity = owners.Capacity; int live = allocated - released;
                    for (int mode = 0; mode < 4; mode++) {
                        wantedKey = &key; if (mode >= 2) wantedKey = &owners[2]; calls = 0; failAt = -1;
                        int baseline = attempts; deny = true;
                        Span<immutable(Owner)> result = new();
                        if (mode % 2 == 0) result = EqualRange<Owner>(owners.AsImmutable(), wantedKey, Compare);
                        else result = EqualRange<Owner>(owners.AsImmutable(), wantedKey);
                        if (result.Length != 3 || result.Data != origin + 1 || calls == 0 || calls > 6) return 1;
                        deny = false; if (attempts != baseline) return 2; int totalCalls = calls;
                        for (int failure = 0; failure < totalCalls; failure++) {
                            result = owners.AsImmutable().Slice(6, 1); calls = 0; failAt = failure; bool caught = false;
                            try {
                                if (mode % 2 == 0) result = EqualRange<Owner>(owners.AsImmutable(), wantedKey, Compare);
                                else result = EqualRange<Owner>(owners.AsImmutable(), wantedKey);
                            } catch (ref Exception error) {
                                caught = error.GetCode() == 77 && CString::Equals(error.GetMessage(), "original comparison");
                            }
                            if (!caught || calls != failure + 1 || result.Data != origin + 6 || result.Length != 1) return 3;
                            if (owners.Count != 7 || owners.Capacity != capacity || made != 8 || dead != 0 || allocated - released != live) return 4;
                            for (nuint i = 0; i < 7; i++)
                                if (&owners[i] != origin + i || owners[i].Id != (int)i || owners[i].Key != keys[i] || (owners[i].Token.Data).reinterpret<nuint>() != tokens[i]) return 5;
                            if (key.Id != 99 || key.Key != 2 || (key.Token.Data).reinterpret<nuint>() != keyToken) return 6;
                        }
                    }
                }
                if (made != 8 || dead != 8 || allocated != released) return 7; return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReturnedElementsCannotBeMutated(bool defaultComparison)
    {
        string call = defaultComparison ? "EqualRange<Value>(values.AsImmutable(), &key)" : "EqualRange<Value>(values.AsImmutable(), &key, Compare)";
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit(Sources($$"""
            using Standard; using Standard::Memory; using Standard::Collections;
            struct Value : IComparable<Value> { public int Key; public immutable int CompareTo(immutable(Value)* other) => 0; }
            int Compare(immutable(Value)* left, immutable(Value)* right) => 0;
            int main() { Value[1] data; Value key = new(); Span<Value> values = new(data, 1);
                var result = {{call}}; result[0].Key = 3; return 0; }
            """)));
        Assert.Contains("Cannot assign to field 'Key' on readonly instance", error.Message);
    }

    private static IEnumerable<SourceFile> Sources(string program)
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Standard"), "*.gf", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path))).Append(new SourceFile("equal-range.gf", program));

    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources(program), optimization);
        Assert.True(result.ExitCode == 0, $"EqualRange exit {result.ExitCode}: {result.StandardError}");
    }
}
