namespace gflat.Tests.Std.Collections;

public sealed partial class MapLookupTests
{
    private static void Run(string program, bool sorted, int optimization)
    {
        string map = sorted ? "SortedMap" : "HashMap";
        string helpers = sorted ? "sorted_map_lookup" : "hash_map_lookup";
        var sources = StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load($"core/Collections/{map}.gf", $"core/Collections/{helpers}.gf"))
            .Append(new SourceFile("map-lookup-tests.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Map lookup exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("MAP_VALUE_LOOKUP.md", 0);

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void ExplicitFallbackNeedsNoDefaultConstructorAndBorrowingStillSupportsNoncopyableValues(bool sorted, int optimization)
    {
        string map = sorted ? "SortedMap" : "HashMap";
        string constructor = sorted ? "new(Compare)" : "new()";
        Run($$"""
            using Standard;
            using Standard::Collections;
            using Standard::Text;

            int live = 0;
            struct Value
            {
                public int Id;
                public String Text;
                public Value(int value) { Id = value; Text = s"value"; live++; }
                public copy Value(immutable(Value)* other) { Id = other.Id; Text = other.Text; live++; }
                ~Value() { live--; }
            }
            struct Noncopyable
            {
                public String Text;
                public Noncopyable(String text) { Text = move text; live++; }
                ~Noncopyable() { live--; }
            }
            int Compare(immutable(int)* left, immutable(int)* right) => *left - *right;
            int main()
            {
                {
                    {{map}}<int, Value> map = {{constructor}};
                    map.Add(1, new Value(11));
                    int key = 1;
                    int missing = 2;
                    Value output = new(77);
                    if (!map.TryGetValue(&key, ref output) || output.Id != 11) return 1;
                    Value result = map.GetValueOrDefault(&key, new Value(33));
                    if (result.Id != 11) return 2;
                    result = map.GetValueOrDefault(&missing, new Value(33));
                    if (result.Id != 33) return 3;
                    {{map}}<int, Noncopyable> owners = {{constructor}};
                    owners.Add(1, new Noncopyable(s"borrowed"));
                    immutable(Noncopyable)* value = owners.FindImmutable(&key)!;
                    if (!value.Text.Equals("borrowed")) return 4;
                }
                if (live != 0) return 5;
                return 0;
            }
            """, sorted, optimization);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public void CopiedLookupsMatchDictionaryValuesAndPreserveMissingOutputs(bool sorted, int optimization)
    {
        var random = new Random(731981);
        var writes = Enumerable.Range(0, 128).Select(_ => (Key: random.Next(-30, 31), Value: random.Next(-10000, 10001))).ToArray();
        IDictionary<int, int> oracle = sorted ? new SortedDictionary<int, int>() : new Dictionary<int, int>();
        foreach (var write in writes) oracle[write.Key] = write.Value;
        int[] queries = Enumerable.Range(-40, 81).ToArray();
        string keys = string.Join(", ", writes.Select(pair => pair.Key));
        string values = string.Join(", ", writes.Select(pair => pair.Value));
        string expected = string.Join(", ", queries.Select(key => oracle.TryGetValue(key, out int value) ? value : 0));
        string found = string.Join(", ", queries.Select(key => oracle.ContainsKey(key) ? 1 : 0));
        string map = sorted ? "SortedMap" : "HashMap";
        string constructor = sorted ? "new(Compare)" : "new()";
        Run($$"""
            using Standard;
            using Standard::Collections;

            int Compare(immutable(int)* left, immutable(int)* right)
            {
                if (*left < *right) return -1;
                if (*left > *right) return 1;
                return 0;
            }
            int main()
            {
                int[128] keys = [{{keys}}];
                int[128] values = [{{values}}];
                int[81] expected = [{{expected}}];
                int[81] found = [{{found}}];
                {{map}}<int, int> map = {{constructor}};
                for (nuint i = 0; i < 128; i++) map.Set(keys[i], values[i]);
                immutable({{map}}<int, int>)* view = &map;
                for (nuint i = 0; i < 81; i++)
                {
                    int key = (int)i - 40;
                    int output = 77;
                    bool present = view.TryGetValue(&key, ref output);
                    if (present != (found[i] != 0)) return 1;
                    if (present && output != expected[i]) return 2;
                    if (!present && output != 77) return 3;
                    int fallback = -777;
                    int expectedFallback = fallback;
                    if (present) expectedFallback = expected[i];
                    if (view.GetValueOrDefault(&key, fallback) != expectedFallback) return 4;
                    if (view.GetValueOrDefault(&key) != expected[i]) return 5;
                    output = 77;
                    if (TryGetValue(view, &key, ref output) != present) return 6;
                    if (GetValueOrDefault(view, &key, fallback) != expectedFallback || GetValueOrDefault(view, &key) != expected[i]) return 7;
                }
                if (map.Count != (nuint){{oracle.Count}}) return 8;
                map.Set(1, 99);
                int key = 1;
                if (!map.TryGetValue(&key, ref key) || key != 99) return 9;
                key = 1;
                int* stored = map.Find(&key)!;
                if (!map.TryGetValue(&key, ref *stored) || *stored != 99) return 10;
                map.Set(2, 55);
                int other = 2;
                stored = map.Find(&other)!;
                if (!map.TryGetValue(&key, ref *stored) || *stored != 99 || map[&key] != 99) return 11;
                return 0;
            }
            """, sorted, optimization);
    }
}
