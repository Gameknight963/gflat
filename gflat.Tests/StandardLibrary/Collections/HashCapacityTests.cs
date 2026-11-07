namespace gflat.Tests.Std.Collections;

public sealed class HashCapacityTests
{
    private static void Run(string program, int optimization)
    {
        var sources = StandardTestSources.Combine(StandardTestSources.Values, StandardTestSources.Load(
            "core/Collections/HashMap.gf", "core/Collections/HashSet.gf"))
            .Append(new SourceFile("hash-capacity.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Hash capacity exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("HASH_CAPACITY.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CollisionsAndDeletionHolesSurviveTrimmingAndFurtherInsertion(int optimization)
        => Run("""
            using Standard;
            using Standard::Collections;

            bool denyCallbacks = false;
            struct Key : IHashable, IEquatable<Key>
            {
                public int Id;
                public Key(int id) { Id = id; }
                public immutable nuint GetHashCode()
                {
                    if (denyCallbacks) throw new Exception("Unexpected hash");
                    return 1;
                }
                public immutable bool Equals(immutable(Key)* other)
                {
                    if (denyCallbacks) throw new Exception("Unexpected equality");
                    return Id == other.Id;
                }
            }
            int main()
            {
                for (int retained = 0; retained <= 32; retained++)
                {
                    HashMap<Key, int> map = new();
                    HashSet<Key> set = new();
                    if (map.EnsureCapacity(80) < 80 || set.EnsureCapacity(80) < 80) return 1;
                    for (int i = 0; i < 64; i++) { map.Add(new Key(i), i * 3); set.Add(new Key(i)); }
                    for (int i = retained; i < 64; i++)
                    {
                        Key key = new(i);
                        if (!map.Remove(&key) || !set.Remove(&key)) return 2;
                    }
                    denyCallbacks = true;
                    map.TrimExcess((nuint)retained + 3); set.TrimExcess((nuint)retained + 3);
                    map.TrimExcess(); set.TrimExcess();
                    nuint expected = (nuint)retained;
                    if (retained > 0 && retained < 4) expected = 4;
                    if (map.Capacity != expected || set.Capacity != expected) return 3;
                    if (map.EnsureCapacity(0) != expected || set.EnsureCapacity(0) != expected) return 4;
                    map.TrimExcess((nuint)0 - (nuint)1); set.TrimExcess((nuint)0 - (nuint)1);
                    denyCallbacks = false;
                    for (int i = 0; i < retained; i++)
                    {
                        Key key = new(i);
                        if (map[&key] != i * 3 || !set.Contains(&key)) return 5;
                    }
                    if (retained > 0)
                    {
                        bool mapCaught = false;
                        bool setCaught = false;
                        try { map.TrimExcess((nuint)retained - 1); }
                        catch (Exception* error) { mapCaught = true; }
                        try { set.TrimExcess((nuint)retained - 1); }
                        catch (Exception* error) { setCaught = true; }
                        if (!mapCaught || !setCaught || map.Count != (nuint)retained || set.Count != (nuint)retained) return 6;
                    }
                    for (int i = retained; i < 64; i++) { map.Add(new Key(i), i * 3); set.Add(new Key(i)); }
                    for (int i = 0; i < 64; i++)
                    {
                        Key key = new(i);
                        if (map[&key] != i * 3 || !set.Contains(&key)) return 7;
                    }
                    map.Clear(); set.Clear(); map.TrimExcess(); set.TrimExcess();
                    if (map.Capacity != 0 || set.Capacity != 0) return 8;
                    map.Add(new Key(100), 100); set.Add(new Key(100));
                }
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableEntriesAndAllocationFailuresPreserveOwners(int optimization)
        => Run("""
            using Standard;
            using Standard::Collections;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            bool deny = false;
            int live = 0;
            int allocations = 0;
            int releases = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    if (deny) return null;
                    void*? result = malloc(bytes);
                    if (result != null) allocations++;
                    return result;
                }
                public replace void Free(void* storage) nothrow { releases++; free(storage); }
            }
            struct Owner : IHashable, IEquatable<Owner>
            {
                public int Id;
                public Owner(int id) { Id = id; live++; }
                public immutable nuint GetHashCode() => 1;
                public immutable bool Equals(immutable(Owner)* other) => Id == other.Id;
                ~Owner() { live--; }
            }
            int main()
            {
                {
                    HashMap<Owner, Owner> map = new();
                    HashSet<Owner> set = new();
                    map.EnsureCapacity(40); set.EnsureCapacity(40);
                    map.Add(new Owner(1), new Owner(2)); set.Add(new Owner(3));
                    deny = true;
                    {
                        bool caught = false;
                        try { map.EnsureCapacity(80); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || map.Capacity != 40 || map.Count != 1 || live != 3) return 9;
                    }
                    {
                        bool caught = false;
                        try { map.TrimExcess(); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || map.Capacity != 40 || map.Count != 1 || live != 3) return 9;
                    }
                    {
                        bool caught = false;
                        try { map.TrimExcess(10); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || map.Capacity != 40 || map.Count != 1 || live != 3) return 9;
                    }
                    {
                        bool caught = false;
                        try { set.EnsureCapacity(80); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || set.Capacity != 40 || set.Count != 1 || live != 3) return 9;
                    }
                    {
                        bool caught = false;
                        try { set.TrimExcess(); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || set.Capacity != 40 || set.Count != 1 || live != 3) return 9;
                    }
                    {
                        bool caught = false;
                        try { set.TrimExcess(10); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || set.Capacity != 40 || set.Count != 1 || live != 3) return 9;
                    }
                    if (map.EnsureCapacity(1) != 40 || set.EnsureCapacity(1) != 40) return 10;
                    deny = false;
                    map.TrimExcess(); set.TrimExcess();
                    if (live != 3 || map.Capacity != 4 || set.Capacity != 4) return 11;
                    {
                        Owner key = new(1);
                        if (map[&key].Id != 2) return 12;
                    }
                    {
                        Owner key = new(3);
                        if (!set.Contains(&key)) return 13;
                    }
                    map.Clear(); set.Clear();
                    deny = true;
                    map.TrimExcess(); set.TrimExcess();
                    if (live != 0 || map.Capacity != 0 || set.Capacity != 0) return 14;
                    deny = false;
                }
                if (allocations != releases) return 15;
                return live;
            }
            """, optimization);
}
