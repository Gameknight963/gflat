namespace gflat.Tests.Std.Collections;

public sealed class SortedCapacityTests
{
    private static void Run(string program, int optimization)
    {
        var result = CompilerTestHelper.Run(StandardTestSources.Combine(StandardTestSources.Values,
            StandardTestSources.Load("core/Collections/Deque.gf", "core/Collections/SortedMap.gf", "core/Collections/SortedSet.gf"))
            .Append(new SourceFile("sorted-capacity.gf", program)), optimization);
        Assert.True(result.ExitCode == 0, $"Sorted capacity exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ThresholdsPreserveComparerOrderAndWrappedDequeContents(int optimization)
        => Run("""
            using Standard;
            using Standard::Collections;

            bool forbidComparison = false;
            int Compare(immutable(int)* left, immutable(int)* right)
            {
                if (forbidComparison) throw new Exception("unexpected comparison");
                return *right - *left;
            }
            int main()
            {
                for (nuint capacity = 0; capacity <= 40; capacity++)
                {
                    for (nuint count = 0; count <= capacity; count++)
                    {
                        SortedMap<int, int> map = new(Compare);
                        SortedSet<int> set = new(Compare);
                        Deque<int> deque = new();
                        map.EnsureCapacity(capacity); set.EnsureCapacity(capacity); deque.EnsureCapacity(capacity);
                        for (nuint i = 0; i < count; i++)
                        {
                            map.Add((int)i, (int)i * 3); set.Add((int)i); deque.AddFirst((int)i);
                        }
                        nuint expected = capacity;
                        if (count < capacity * 9 / 10) expected = count;
                        forbidComparison = true;
                        map.TrimExcess(); set.TrimExcess(); deque.TrimExcess();
                        if (map.Capacity != expected || set.Capacity != expected || deque.Capacity != expected) return 1;
                        if (map.EnsureCapacity(0) != expected || set.EnsureCapacity(0) != expected || deque.EnsureCapacity(0) != expected) return 2;
                        forbidComparison = false;
                        for (nuint i = 0; i < count; i++)
                        {
                            int key = (int)i;
                            if (map[&key] != key * 3 || !set.Contains(&key) || deque.RemoveLast() != key) return 3;
                        }
                        var iterator = map.GetEnumerator();
                        nuint remaining = count;
                        while (iterator.MoveNext())
                        {
                            remaining--;
                            if (*iterator.Current.Key != (int)remaining) return 4;
                        }
                        if (remaining != 0) return 5;
                        map.Clear(); set.Clear();
                        forbidComparison = true;
                        map.ShrinkToFit(); set.ShrinkToFit(); deque.ShrinkToFit();
                        forbidComparison = false;
                        if (map.Capacity != 0 || set.Capacity != 0 || deque.Capacity != 0) return 6;
                    }
                }
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FailedGrowthAndTrimPreserveNoncopyableOwnersAndNoComparerIsNeeded(int optimization)
        => Run("""
            using Standard;
            using Standard::Collections;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int live = 0;
            bool deny = false;
            bool forbidComparison = false;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes) { if (deny) return null; return malloc(bytes); }
                public replace void Free(void* storage) nothrow { free(storage); }
            }
            struct Owner
            {
                public int Id;
                public Owner(int id) { Id = id; live++; }
                ~Owner() { live--; }
            }
            int Compare(immutable(Owner)* left, immutable(Owner)* right)
            {
                if (forbidComparison) throw new Exception("unexpected comparison");
                return left.Id - right.Id;
            }
            int main()
            {
                {
                    SortedMap<Owner, Owner> map = new(Compare);
                    SortedSet<Owner> set = new(Compare);
                    Deque<Owner> deque = new();
                    map.EnsureCapacity(40); set.EnsureCapacity(40); deque.EnsureCapacity(40);
                    map.Add(new Owner(1), new Owner(2)); set.Add(new Owner(3)); deque.AddFirst(new Owner(4));
                    deny = true; forbidComparison = true;
                    {
                        bool caught = false;
                        try { map.EnsureCapacity(80); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || map.Count != 1 || map.Capacity != 40 || live != 4) return 7;
                    }
                    {
                        bool caught = false;
                        try { map.TrimExcess(); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || map.Count != 1 || map.Capacity != 40 || live != 4) return 7;
                    }
                    {
                        bool caught = false;
                        try { map.ShrinkToFit(); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || map.Count != 1 || map.Capacity != 40 || live != 4) return 7;
                    }
                    {
                        bool caught = false;
                        try { set.EnsureCapacity(80); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || set.Count != 1 || set.Capacity != 40 || live != 4) return 7;
                    }
                    {
                        bool caught = false;
                        try { set.TrimExcess(); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || set.Count != 1 || set.Capacity != 40 || live != 4) return 7;
                    }
                    {
                        bool caught = false;
                        try { set.ShrinkToFit(); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || set.Count != 1 || set.Capacity != 40 || live != 4) return 7;
                    }
                    {
                        bool caught = false;
                        try { deque.EnsureCapacity(80); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || deque.Count != 1 || deque.Capacity != 40 || live != 4) return 7;
                    }
                    {
                        bool caught = false;
                        try { deque.TrimExcess(); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || deque.Count != 1 || deque.Capacity != 40 || live != 4) return 7;
                    }
                    {
                        bool caught = false;
                        try { deque.ShrinkToFit(); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || deque.Count != 1 || deque.Capacity != 40 || live != 4) return 7;
                    }
                    deny = false;
                    map.TrimExcess(); set.TrimExcess(); deque.TrimExcess();
                    if (map.Capacity != 1 || set.Capacity != 1 || deque.Capacity != 1 || live != 4) return 8;
                    var entries = map.GetEnumerator();
                    if (!entries.MoveNext() || entries.Current.Key.Id != 1 || entries.Current.Value.Id != 2) return 9;
                    if (set.Min.Id != 3 || deque.Front.Id != 4) return 10;
                    map.Clear(); set.Clear(); deque.Clear();
                    deny = true;
                    map.ShrinkToFit(); set.ShrinkToFit(); deque.ShrinkToFit();
                    if (live != 0 || map.Capacity != 0 || set.Capacity != 0 || deque.Capacity != 0) return 11;
                    deny = false; forbidComparison = false;
                }
                return live;
            }
            """, optimization);
}
