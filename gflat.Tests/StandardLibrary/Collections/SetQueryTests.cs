namespace gflat.Tests.Std.Collections;

public sealed class SetQueryTests
{
    private static IEnumerable<SourceFile> Sources(string code) => StandardTestSources.Combine(StandardTestSources.Values,
        StandardTestSources.Load("core/Collections/HashMap.gf", "core/Collections/HashSet.gf",
            "core/Collections/SortedMap.gf", "core/Collections/SortedSet.gf",
            "core/Collections/hash_set_queries.gf", "core/Collections/sorted_set_queries.gf", "core/Queries/WhereSource.gf",
            "core/Queries/SelectSource.gf", "core/Queries/enumerable_where.gf", "core/Queries/enumerable_select.gf",
            "core/Queries/enumerable_count.gf", "core/Queries/enumerable_to_vector.gf"))
        .Append(new SourceFile("set-queries.gf", code));

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("SET_QUERIES.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void QueriesAreLazyAndFreshEnumerationsSeeMutationsAndComparatorOrder(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard;
            using Standard::Collections;
            using Standard::Queries;

            int calls = 0;
            int Descending(immutable(int)* left, immutable(int)* right)
            {
                if (*left > *right) return -1;
                if (*left < *right) return 1;
                return 0;
            }
            bool Odd(immutable(int)* value) { calls++; return *value % 2 != 0; }
            int Read(immutable(int)* value) => *value;
            int CountHash(immutable(HashSet<int>)* source) => (int)source.AsEnumerable().Count();
            int CountSorted(immutable(SortedSet<int>)* source) => (int)source.AsEnumerable().Count();
            int main()
            {
                HashSet<int> hash = new();
                SortedSet<int> sorted = new(Descending);
                var hashQuery = hash.AsEnumerable().Where(Odd);
                var sortedQuery = sorted.AsEnumerable().Where(Odd);
                if (calls != 0 || hashQuery.Count() != 0 || sortedQuery.Count() != 0 || calls != 0) return 1;
                for (int i = 1; i <= 5; i++) { hash.Add(i); sorted.Add(i); }
                if (hashQuery.Count() != 3 || sortedQuery.Count() != 3 || calls != 10) return 2;
                if (CountHash(&hash) != 5 || CountSorted(&sorted) != 5) return 3;
                Vector<int> selected = sorted.AsEnumerable().Where(Odd).Select<int>(Read).ToVector();
                if (selected.Count != 3 || selected[0] != 5 || selected[1] != 3 || selected[2] != 1) return 4;
                int three = 3;
                hash.Remove(&three); sorted.Remove(&three);
                hash.Reserve(128); sorted.Reserve(128);
                hash.Add(7); sorted.Add(7);
                if (hashQuery.Count() != 3 || sortedQuery.Count() != 3) return 5;
                selected = sortedQuery.Select<int>(Read).ToVector();
                if (selected.Count != 3 || selected[0] != 7 || selected[1] != 5 || selected[2] != 1) return 6;
                hash.Clear(); sorted.Clear();
                if (hashQuery.Count() != 0 || sortedQuery.Count() != 0) return 7;
                hash.Add(9); sorted.Add(9);
                if (hashQuery.Count() != 1 || sortedQuery.Count() != 1) return 8;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Set query exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BorrowedNoncopyableElementsKeepIdentityWithoutAllocations(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard;
            using Standard::Collections;
            using Standard::Queries;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int attempts = 0;
            int live = 0;
            int owners = 0;
            bool deny = false;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    attempts++;
                    if (deny) return null;
                    void*? result = malloc(bytes);
                    if (result != null) live++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { live--; free(storage); }
            }
            struct Key : IHashable, IEquatable<Key>
            {
                public int Id;
                public Key(int id) { Id = id; owners++; }
                public immutable nuint GetHashCode() nothrow => 1;
                public immutable bool Equals(immutable(Key)* other) nothrow => Id == other.Id;
                ~Key() { owners--; }
            }
            int Ascending(immutable(Key)* left, immutable(Key)* right)
            {
                if (left.Id < right.Id) return -1;
                if (left.Id > right.Id) return 1;
                return 0;
            }
            bool Positive(immutable(Key)* value) => value.Id > 0;
            int Sum(immutable(HashSet<Key>)* source)
            {
                int total = 0;
                foreach (immutable(Key)* key in source.AsEnumerable().Where(Positive)) total += key.Id;
                return total;
            }
            int main()
            {
                {
                    HashSet<Key> hash = new();
                    SortedSet<Key> sorted = new(Ascending);
                    for (int i = 1; i <= 4; i++) { hash.Add(new Key(i)); sorted.Add(new Key(i)); }
                    Key probe = new(2);
                    immutable(Key)*? stored = hash.Find(&probe);
                    int before = attempts;
                    deny = true;
                    if (Sum(&hash) != 10 || sorted.AsEnumerable().Where(Positive).Count() != 4) return 1;
                    bool found = false;
                    foreach (immutable(Key)* key in hash.AsEnumerable())
                        if (key == stored) found = true;
                    if (!found || attempts != before || owners != 9) return 2;
                    int expected = 1;
                    foreach (immutable(Key)* key in sorted.AsEnumerable())
                    {
                        if (key.Id != expected) return 3;
                        expected++;
                    }
                    if (expected != 5 || attempts != before) return 4;
                    deny = false;
                }
                if (owners != 0 || live != 0) return 5;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Set owner query exit {result.ExitCode}: {result.StandardError}");
    }
}
