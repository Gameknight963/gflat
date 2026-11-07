namespace gflat.Tests.Std.Collections;

public sealed class MapCollectionViewTests
{
    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("MAP_COLLECTION_VIEWS.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ImmutableViewsBorrowNoncopyableOwnersWithoutAllocating(int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Collections/HashMap.gf", "core/Collections/SortedMap.gf"))
            .Append(new SourceFile("map-view-owners.gf", """
            using Standard;
            using Standard::Collections;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int live = 0;
            int attempts = 0;
            bool deny = false;
            int keysDestroyed = 0;
            int valuesDestroyed = 0;
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
                public Key(int id) { Id = id; }
                public immutable nuint GetHashCode() nothrow => 1;
                public immutable bool Equals(immutable(Key)* other) nothrow => Id == other.Id;
                ~Key() { keysDestroyed++; }
            }
            struct Value
            {
                public int Amount;
                public Value(int amount) { Amount = amount; }
                ~Value() { valuesDestroyed++; }
            }
            int Compare(immutable(Key)* left, immutable(Key)* right)
            {
                if (left.Id < right.Id) return -1;
                if (left.Id > right.Id) return 1;
                return 0;
            }
            int main()
            {
                {
                    HashMap<Key, Value> hash = new();
                    SortedMap<Key, Value> sorted = new(Compare);
                    for (int i = 0; i < 8; i++)
                    {
                        hash.Add(new Key(i), new Value(i * 3));
                        sorted.Add(new Key(i), new Value(i * 3));
                    }
                    immutable(HashMap<Key, Value>)* immutableHash = &hash;
                    immutable(SortedMap<Key, Value>)* immutableSorted = &sorted;
                    int before = attempts;
                    deny = true;
                    int seen = 0;
                    foreach (immutable(Key)* key in immutableHash.Keys)
                    {
                        if (hash.FindKey(key) != key) return 1;
                        seen = seen | (1 << key.Id);
                    }
                    if (seen != 255) return 2;
                    int sum = 0;
                    foreach (immutable(Value)* value in immutableHash.Values) sum += value.Amount;
                    if (sum != 84) return 3;
                    int next = 0;
                    foreach (immutable(Key)* key in immutableSorted.Keys)
                    {
                        if (key.Id != next) return 4;
                        next++;
                    }
                    next = 0;
                    foreach (immutable(Value)* value in immutableSorted.Values)
                    {
                        if (value.Amount != next * 3) return 5;
                        next++;
                    }
                    HashMap<Key, Value>::KeyEnumerator keys = immutableHash.Keys.GetEnumerator();
                    HashMap<Key, Value>::ValueEnumerator values = immutableHash.Values.GetEnumerator();
                    if (!keys.MoveNext() || !values.MoveNext()) return 6;
                    immutable(Value)* borrowed = values.Current;
                    Value*? mutable = hash.Find(keys.Current);
                    if (mutable == null) return 7;
                    mutable!.Amount = 42;
                    if (borrowed.Amount != 42) return 8;
                    deny = false;
                    if (attempts != before || keysDestroyed != 0 || valuesDestroyed != 0) return 9;
                }
                if (keysDestroyed != 16 || valuesDestroyed != 16 || live != 0) return 10;
                return 0;
            }
            """)), optimization);
        Assert.True(result.ExitCode == 0, $"Map view ownership exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SavedViewsObserveCurrentContentsAndSortedOrderAfterStorageChanges(int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Collections/HashMap.gf", "core/Collections/SortedMap.gf"))
            .Append(new SourceFile("map-collection-views.gf", """
            using Standard;
            using Standard::Collections;

            int Compare(immutable(int)* left, immutable(int)* right)
            {
                if (*left > *right) return -1;
                if (*left < *right) return 1;
                return 0;
            }
            int main()
            {
                HashMap<int, int> hash = new();
                SortedMap<int, int> sorted = new(Compare);
                HashMap<int, int>::KeyCollection hashKeys = hash.Keys;
                HashMap<int, int>::ValueCollection hashValues = hash.Values;
                SortedMap<int, int>::KeyCollection sortedKeys = sorted.Keys;
                SortedMap<int, int>::ValueCollection sortedValues = sorted.Values;
                if (!hashKeys.IsEmpty || !hashValues.IsEmpty || !sortedKeys.IsEmpty || !sortedValues.IsEmpty) return 1;
                for (int count = 1; count <= 12; count++)
                {
                    hash.Add(count - 1, (count - 1) * 3 + 1);
                    sorted.Add(count - 1, (count - 1) * 3 + 1);
                    hash.Reserve((nuint)count * 4);
                    sorted.Reserve((nuint)count * 4);
                    if (hashKeys.Count != (nuint)count || hashValues.Count != (nuint)count || sortedKeys.Count != (nuint)count || sortedValues.Count != (nuint)count) return 2;
                    int seen = 0;
                    foreach (immutable(int)* key in hashKeys) seen = seen | (1 << *key);
                    if (seen != (1 << count) - 1) return 3;
                    seen = 0;
                    foreach (immutable(int)* value in hashValues) seen = seen | (1 << ((*value - 1) / 3));
                    if (seen != (1 << count) - 1) return 4;
                    int next = count - 1;
                    foreach (immutable(int)* key in sortedKeys) { if (*key != next) return 5; next--; }
                    if (next != -1) return 6;
                    next = count - 1;
                    foreach (immutable(int)* value in sortedValues) { if (*value != next * 3 + 1) return 7; next--; }
                    if (next != -1) return 8;
                }
                int remove = 5;
                hash.Remove(&remove); sorted.Remove(&remove);
                hash.TrimExcess(); sorted.TrimExcess();
                if (hashKeys.Count != 11 || hashValues.Count != 11 || sortedKeys.Count != 11 || sortedValues.Count != 11) return 9;
                foreach (immutable(int)* key in hashKeys) if (*key == 5) return 10;
                foreach (immutable(int)* key in sortedKeys) if (*key == 5) return 11;
                HashMap<int, int>::KeyEnumerator iterator = hashKeys.GetEnumerator();
                bool caught = false;
                try { immutable(int)* invalid = iterator.Current; }
                catch (Exception* error) { caught = true; }
                if (!caught) return 12;
                while (iterator.MoveNext()) { }
                caught = false;
                try { immutable(int)* invalid = iterator.Current; }
                catch (Exception* error) { caught = true; }
                if (!caught) return 13;
                hash.Clear(); sorted.Clear();
                if (!hashKeys.IsEmpty || !hashValues.IsEmpty || !sortedKeys.IsEmpty || !sortedValues.IsEmpty) return 14;
                foreach (immutable(int)* key in hashKeys) return 15;
                foreach (immutable(int)* value in sortedValues) return 16;
                return 0;
            }
            """)), optimization);
        Assert.True(result.ExitCode == 0, $"Map views exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }
}
