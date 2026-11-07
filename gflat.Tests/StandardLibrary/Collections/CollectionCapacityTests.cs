namespace gflat.Tests.Std.Collections;

public sealed class CollectionCapacityTests
{
    private static void Run(string program, int optimization)
    {
        var sources = StandardTestSources.Combine(StandardTestSources.Values, StandardTestSources.Load(
            "core/Collections/Queue.gf", "core/Collections/Stack.gf", "core/Collections/PriorityQueue.gf"))
            .Append(new SourceFile("collection-capacity.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Capacity exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("COLLECTION_CAPACITY.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ThresholdsAndWrappedQueueOrderArePreserved(int optimization)
        => Run("""
            using Standard;
            using Standard::Collections;

            int Compare(immutable(int)* left, immutable(int)* right) => *left - *right;
            int main()
            {
                for (nuint capacity = 0; capacity <= 40; capacity++)
                {
                    for (nuint count = 0; count <= capacity; count++)
                    {
                        Vector<int> vector = new();
                        Queue<int> queue = new();
                        Stack<int> stack = new();
                        PriorityQueue<int> heap = new(Compare);
                        if (vector.EnsureCapacity(capacity) < capacity || queue.EnsureCapacity(capacity) < capacity ||
                            stack.EnsureCapacity(capacity) < capacity || heap.EnsureCapacity(capacity) < capacity) return 1;
                        // Advance the ring head while retaining the reserved buffer.
                        for (nuint i = 0; i < capacity; i++) queue.Enqueue(-1);
                        for (nuint i = 0; i < capacity / 2; i++) { int unused = queue.Dequeue(); }
                        queue.Clear();
                        for (nuint i = 0; i < count; i++)
                        {
                            vector.Add((int)i); queue.Enqueue((int)i); stack.Push((int)i); heap.Enqueue((int)i);
                        }
                        if (count != 0)
                        {
                            int first = queue.Dequeue();
                            queue.Enqueue(first + (int)count);
                        }
                        nuint expected = capacity;
                        if (count < capacity * 9 / 10) expected = count;
                        vector.TrimExcess(); queue.TrimExcess(); stack.TrimExcess(); heap.TrimExcess();
                        if (vector.Capacity != expected || queue.Capacity != expected || stack.Capacity != expected || heap.Capacity != expected) return 2;
                        if (vector.EnsureCapacity(0) != expected || queue.EnsureCapacity(0) != expected ||
                            stack.EnsureCapacity(0) != expected || heap.EnsureCapacity(0) != expected) return 3;
                        for (nuint i = 0; i < count; i++)
                        {
                            if (vector[i] != (int)i || queue.Dequeue() != (int)(i + 1) ||
                                stack.Pop() != (int)(count - i - 1) || heap.Dequeue() != (int)i) return 4;
                        }
                        vector.Clear();
                        vector.ShrinkToFit(); queue.ShrinkToFit(); stack.ShrinkToFit(); heap.ShrinkToFit();
                        if (vector.Capacity != 0 || queue.Capacity != 0 || stack.Capacity != 0 || heap.Capacity != 0) return 5;
                    }
                }
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableOwnersSurviveFailedGrowthAndTrimming(int optimization)
        => Run("""
            using Standard;
            using Standard::Collections;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            bool fail = false;
            int live = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    if (fail) return null;
                    return malloc(bytes);
                }
                public replace void Free(void* storage) nothrow { free(storage); }
            }
            struct Owner
            {
                public int Id;
                public Owner(int id) { Id = id; live++; }
                ~Owner() { live--; }
            }
            int Compare(immutable(Owner)* left, immutable(Owner)* right) => left.Id - right.Id;
            int main()
            {
                {
                    Vector<Owner> vector = new();
                    vector.EnsureCapacity(20);
                    vector.Add(new Owner(42));
                    Queue<Owner> queue = new();
                    queue.EnsureCapacity(20);
                    queue.Enqueue(new Owner(42));
                    Stack<Owner> stack = new();
                    stack.EnsureCapacity(20);
                    stack.Push(new Owner(42));
                    PriorityQueue<Owner> heap = new(Compare);
                    heap.EnsureCapacity(20);
                    heap.Enqueue(new Owner(42));
                    fail = true;
                    {
                        bool caught = false;
                        try { vector.EnsureCapacity(40); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || vector.Capacity != 20 || vector.Count != 1 || vector[0].Id != 42 || live != 4) return 6;
                    }
                    {
                        bool caught = false;
                        try { vector.TrimExcess(); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || vector.Capacity != 20 || vector.Count != 1 || vector[0].Id != 42 || live != 4) return 6;
                    }
                    {
                        bool caught = false;
                        try { queue.EnsureCapacity(40); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || queue.Capacity != 20 || queue.Count != 1 || queue.Front.Id != 42 || live != 4) return 6;
                    }
                    {
                        bool caught = false;
                        try { queue.TrimExcess(); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || queue.Capacity != 20 || queue.Count != 1 || queue.Front.Id != 42 || live != 4) return 6;
                    }
                    {
                        bool caught = false;
                        try { stack.EnsureCapacity(40); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || stack.Capacity != 20 || stack.Count != 1 || stack.Top.Id != 42 || live != 4) return 6;
                    }
                    {
                        bool caught = false;
                        try { stack.TrimExcess(); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || stack.Capacity != 20 || stack.Count != 1 || stack.Top.Id != 42 || live != 4) return 6;
                    }
                    {
                        bool caught = false;
                        try { heap.EnsureCapacity(40); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || heap.Capacity != 20 || heap.Count != 1 || heap.Top.Id != 42 || live != 4) return 6;
                    }
                    {
                        bool caught = false;
                        try { heap.TrimExcess(); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || heap.Capacity != 20 || heap.Count != 1 || heap.Top.Id != 42 || live != 4) return 6;
                    }
                    fail = false;
                    vector.TrimExcess(); queue.TrimExcess(); stack.TrimExcess(); heap.TrimExcess();
                    if (live != 4 || vector.Capacity != 1 || queue.Capacity != 1 || stack.Capacity != 1 || heap.Capacity != 1) return 7;
                }
                return live;
            }
            """, optimization);
}
