namespace gflat.Tests.Std.Collections;

public sealed class MapCollectionQueryTests
{
    [Fact]
    public void GuideQueryExampleCompiles() => StandardLibraryGuideTests.CheckProgram("MAP_COLLECTION_VIEWS.md", 1);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void QueriesBorrowNoncopyableValuesWithoutAllocating(int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Collections/HashMap.gf", "core/Collections/SortedMap.gf",
                "core/Collections/map_collection_queries.gf", "core/Queries/WhereSource.gf",
                "core/Queries/enumerable_where.gf", "core/Queries/enumerable_count.gf"))
            .Append(new SourceFile("map-view-query-owners.gf", """
            using Standard;
            using Standard::Collections;
            using Standard::Queries;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            bool deny = false;
            int attempts = 0;
            int destroyed = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    attempts++;
                    if (deny) return null;
                    return malloc(bytes);
                }
                public replace void Free(void* storage) nothrow => free(storage);
            }
            struct Owner
            {
                public int Value;
                public Owner(int value) { Value = value; }
                ~Owner() { destroyed++; }
            }
            int Compare(immutable(int)* left, immutable(int)* right)
            {
                if (*left < *right) return -1;
                if (*left > *right) return 1;
                return 0;
            }
            bool Positive(immutable(Owner)* value) => value.Value > 0;
            int main()
            {
                {
                    HashMap<int, Owner> hash = new();
                    SortedMap<int, Owner> sorted = new(Compare);
                    hash.Add(1, new Owner(10));
                    sorted.Add(1, new Owner(20));
                    int before = attempts;
                    deny = true;
                    if (hash.Values.AsEnumerable().Where(Positive).Count() != 1) return 1;
                    if (sorted.Values.AsEnumerable().Where(Positive).Count() != 1) return 2;
                    if (attempts != before || destroyed != 0) return 3;
                    deny = false;
                }
                if (destroyed != 2) return 4;
                return 0;
            }
            """)), optimization);
        Assert.True(result.ExitCode == 0, $"Map owner queries exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ViewQueriesRemainLazyAndObserveCurrentMapContents(int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Collections/HashMap.gf", "core/Collections/SortedMap.gf",
                "core/Collections/map_collection_queries.gf", "core/Queries/WhereSource.gf", "core/Queries/SelectSource.gf",
                "core/Queries/enumerable_where.gf", "core/Queries/enumerable_select.gf", "core/Queries/enumerable_count.gf",
                "core/Queries/enumerable_to_vector.gf"))
            .Append(new SourceFile("map-view-queries.gf", """
            using Standard;
            using Standard::Collections;
            using Standard::Queries;

            int calls = 0;
            int Compare(immutable(int)* left, immutable(int)* right)
            {
                if (*left < *right) return -1;
                if (*left > *right) return 1;
                return 0;
            }
            bool Even(immutable(int)* value) { calls++; return *value % 2 == 0; }
            int Read(immutable(int)* value) { calls++; return *value; }
            int main()
            {
                HashMap<int, int> hash = new();
                SortedMap<int, int> sorted = new(Compare);
                hash.Add(1, 10); sorted.Add(1, 10);
                Enumerable<immutable(int)*, HashMap<int, int>::KeyCollection, HashMap<int, int>::KeyEnumerator> keys = hash.Keys.AsEnumerable();
                Enumerable<immutable(int)*, SortedMap<int, int>::ValueCollection, SortedMap<int, int>::ValueEnumerator> values = sorted.Values.AsEnumerable();
                hash.Add(2, 20); sorted.Add(2, 20);
                if (keys.Count() != 2 || values.Count() != 2 || calls != 0) return 1;
                Vector<int> selected = hash.Keys.AsEnumerable().Where(Even).Select<int>(Read).ToVector();
                if (selected.Count != 1 || selected[0] != 2 || calls != 3) return 2;
                calls = 0;
                selected = values.Select<int>(Read).ToVector();
                if (selected.Count != 2 || selected[0] != 10 || selected[1] != 20 || calls != 2) return 3;
                hash.Reserve(64); sorted.Reserve(64);
                hash.Add(3, 30); sorted.Add(3, 30);
                if (keys.Count() != 3 || values.Count() != 3) return 4;
                if (hash.Values.AsEnumerable().Count() != 3 || sorted.Keys.AsEnumerable().Count() != 3) return 5;
                hash.Clear(); sorted.Clear();
                if (keys.Count() != 0 || values.Count() != 0) return 6;
                return 0;
            }
            """)), optimization);
        Assert.True(result.ExitCode == 0, $"Map view queries exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
