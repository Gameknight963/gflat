namespace gflat.Tests.Std.Collections;

public sealed class MapValueSearchTests
{
    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Collections/HashMap.gf", "core/Collections/SortedMap.gf", "core/Collections/EqualityComparer.gf",
                "core/Collections/hash_map_value_search.gf", "core/Collections/sorted_map_value_search.gf"))
            .Append(new SourceFile("map-value-search.gf", program)), optimization);
        Assert.True(result.ExitCode == 0, $"Map value search exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("MAP_VALUE_SEARCH.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MutationAndDeletionHolesMatchDictionaryContainsValue(int optimization)
    {
        string path = Path.Combine(Path.GetTempPath(), "gflat_map_value_" + Guid.NewGuid().ToString("N"));
        try
        {
            var map = new Dictionary<int, int>();
            var random = new Random(927184);
            using (var writer = new BinaryWriter(File.Create(path)))
            for (int step = 0; step < 512; step++)
            {
                int operation = random.Next(0, 16), key = random.Next(0, 32), value = random.Next(-8, 9), query = random.Next(-10, 11);
                if (operation == 0) map.Clear();
                else if (operation < 6) map.Remove(key);
                else map[key] = value;
                foreach (int item in new[] { operation, key, value, query, map.ContainsValue(query) ? 1 : 0 }) writer.Write(item);
            }
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            Run($$"""
                using Standard;
                using Standard::Collections;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                int Compare(immutable(int)* left, immutable(int)* right) => *right - *left;
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    HashMap<int, int> hashed = new();
                    SortedMap<int, int> sorted = new(Compare);
                    hashed.Reserve(40); sorted.Reserve(40);
                    int[5] row = default(int[5]);
                    for (int step = 0; step < 512; step++)
                    {
                        if (fread(&row[0], 4, 5, file!) != 5) return 2;
                        int key = row[1];
                        if (row[0] == 0) { hashed.Clear(); sorted.Clear(); }
                        else if (row[0] < 6) { hashed.Remove(&key); sorted.Remove(&key); }
                        else { hashed.Set(key, row[2]); sorted.Set(key, row[2]); }
                        int query = row[3];
                        bool expected = row[4] == 1;
                        if (hashed.ContainsValue(&query) != expected || ContainsValue(&sorted, &query) != expected) return 3;
                    }
                    return 0;
                }
                """, optimization);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableValuesAndComparisonFailuresNeedNoKeyCallbacks(int optimization)
        => Run("""
            using Standard;
            using Standard::Collections;

            bool forbidKeys = false;
            int live = 0;
            int calls = 0;
            int failAt = 0;
            struct Key : IHashable, IEquatable<Key>
            {
                public int Id;
                public Key(int id) { Id = id; }
                public immutable nuint GetHashCode() { if (forbidKeys) throw new Exception("key hash"); return 1; }
                public immutable bool Equals(immutable(Key)* other) { if (forbidKeys) throw new Exception("key equality"); return Id == other.Id; }
            }
            struct Owner
            {
                public int Id;
                public Owner(int id) { Id = id; live++; }
                ~Owner() { live--; }
            }
            int Compare(immutable(Key)* left, immutable(Key)* right)
            {
                if (forbidKeys) throw new Exception("key order");
                return left.Id - right.Id;
            }
            bool Equal(immutable(Owner)* left, immutable(Owner)* right)
            {
                calls++;
                if (calls == failAt) throw new Exception("value equality");
                return left.Id == right.Id;
            }
            int main()
            {
                {
                    HashMap<Key, Owner> hashed = new();
                    SortedMap<Key, Owner> sorted = new(Compare);
                    for (int i = 1; i <= 3; i++) { hashed.Add(new Key(i), new Owner(i)); sorted.Add(new Key(i), new Owner(i)); }
                    Owner query = new(99);
                    Key key = new(2);
                    immutable(Owner)* borrowed = hashed.FindImmutable(&key)!;
                    forbidKeys = true;
                    calls = 0;
                    if (!hashed.ContainsValue(borrowed, Equal) || calls > 3 || live != 7) return 1;
                    for (int failure = 1; failure <= 3; failure++)
                    {
                        failAt = failure;
                        for (int form = 0; form < 2; form++)
                        {
                            calls = 0;
                            bool caught = false;
                            try
                            {
                                if (form == 0) hashed.ContainsValue(&query, Equal);
                                else sorted.ContainsValue(&query, Equal);
                            }
                            catch (Exception* error) { caught = true; }
                            if (!caught || calls != failure || hashed.Count != 3 || sorted.Count != 3 || live != 7) return 2;
                        }
                    }
                    failAt = 0; query.Id = 1; calls = 0;
                    if (!sorted.ContainsValue(&query, Equal) || calls != 1) return 3;
                    hashed.Clear(); sorted.Clear(); calls = 0;
                    if (hashed.ContainsValue(&query, Equal) || sorted.ContainsValue(&query, Equal) || calls != 0 || live != 1) return 4;
                    forbidKeys = false;
                }
                return live;
            }
            """, optimization);
}
