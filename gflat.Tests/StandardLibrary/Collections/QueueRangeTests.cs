namespace gflat.Tests.Std.Collections;

public sealed class QueueRangeTests
{
    private static void Run(string code, int optimization)
    {
        var sources = StandardTestSources.Combine(StandardTestSources.Values, StandardTestSources.Load(
            "core/Collections/Queue.gf", "core/Collections/queue_enqueue_range.gf"))
            .Append(new SourceFile("queue-range.gf", code));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Queue range exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("QUEUE_RANGE.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void WrappedAndGrowingBatchesRetainFifoOrderAndSelfEnumerationIsCaptured(int optimization)
        => Run(Tracking + """
            int main()
            {
                for (int form = 0; form < 2; form++)
                for (nuint capacity = 1; capacity <= 17; capacity++)
                for (nuint skipped = 0; skipped < capacity; skipped++)
                for (nuint length = 0; length <= 24; length++)
                {
                    Queue<int> queue = new();
                    queue.Reserve(capacity);
                    for (nuint i = 0; i < capacity; i++) queue.Enqueue((int)i);
                    for (nuint i = 0; i < skipped; i++)
                        if (queue.Dequeue() != (int)i) return 1;
                    for (nuint i = 0; i < skipped; i++) queue.Enqueue((int)(capacity + i));
                    int[24] batch;
                    for (nuint i = 0; i < length; i++) batch[i] = 100 + (int)i;
                    int before = attempts;
                    if (form == 0)
                    {
                        if (length % 2 == 0) queue.EnqueueRange(new Span<immutable(int)>(&batch[0], length));
                        else Standard::Collections::EnqueueRange(&queue, new Span<immutable(int)>(&batch[0], length));
                        if (attempts - before > 2) return 8;
                    }
                    else
                    {
                        Vector<int> input = new();
                        for (nuint i = 0; i < length; i++) input.Add(batch[i]);
                        if (length % 2 == 0) queue.EnqueueRange(input.AsEnumerable());
                        else Standard::Collections::EnqueueRange(&queue, input.AsEnumerable());
                    }
                    if (length == 0 && attempts != before) return 2;
                    if (queue.Count != capacity + length) return 3;
                    for (nuint i = skipped; i < capacity + skipped; i++)
                        if (queue.Dequeue() != (int)i) return 4;
                    for (nuint i = 0; i < length; i++)
                        if (queue.Dequeue() != 100 + (int)i) return 5;
                    for (int i = 0; i < 7; i++) queue.Enqueue(i);
                    queue.EnqueueRange(queue.AsEnumerable());
                    for (int i = 0; i < 14; i++) if (queue.Dequeue() != i % 7) return 6;
                    queue.Enqueue(42);
                    queue.EnqueueRange(new Span<immutable(int)>(&queue.Front, 1));
                    if (queue.Dequeue() != 42 || queue.Dequeue() != 42) return 7;
                }
                return live;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryAllocationAndCopyFailurePreservesWrappedQueueAndAllOwners(int optimization)
        => Run(Tracking + """
            int owners = 0;
            int copies = 0;
            int failCopy = 0;
            struct Owner
            {
                public int Id;
                public String Text;
                public Owner(int id) { Id = id; Text = s"owned"; owners++; }
                public copy Owner(immutable(Owner)* source)
                {
                    Id = source.Id; Text = source.Text;
                    copies++;
                    if (copies == failCopy) throw new Exception("copy");
                    owners++;
                }
                ~Owner() { owners--; }
            }
            void Apply(Queue<Owner>* queue, Vector<Owner>* input, int form)
            {
                if (form == 0) queue.EnqueueRange(input.AsImmutable());
                else queue.EnqueueRange(input.AsEnumerable());
            }
            int main()
            {
                for (int form = 0; form < 2; form++)
                for (int reserved = 0; reserved < 2; reserved++)
                {
                    int observed = 0;
                    for (int point = 0; point <= observed; point++)
                    {
                        Queue<Owner> queue = new();
                        queue.Reserve(4);
                        for (int i = 0; i < 4; i++) queue.Enqueue(new Owner(i));
                        { Owner removed = queue.Dequeue(); }
                        queue.Enqueue(new Owner(4));
                        if (reserved != 0) queue.Reserve(32);
                        nuint capacity = queue.Capacity;
                        nuint first = (&queue.Front).reinterpret<nuint>();
                        Vector<Owner> input = new();
                        for (int i = 5; i < 14; i++) input.Add(new Owner(i));
                        int baseline = live;
                        attempts = 0;
                        failAt = point;
                        bool caught = false;
                        try { Apply(&queue, &input, form); }
                        catch (Exception* error) { caught = true; }
                        failAt = 0;
                        if (point == 0)
                        {
                            observed = attempts;
                            if (caught || owners != 22 || queue.Count != 13) return 1;
                            if (reserved != 0 && ((&queue.Front).reinterpret<nuint>() != first || queue.Capacity != capacity)) return 9;
                        }
                        else
                        {
                            if (!caught || live != baseline || owners != 13 || queue.Count != 4 || queue.Capacity != capacity) return 2;
                            if ((&queue.Front).reinterpret<nuint>() != first) return 3;
                        }
                        for (int i = 1; i <= 4; i++) { Owner item = queue.Dequeue(); if (item.Id != i) return 4; }
                        if (point == 0)
                            for (int i = 5; i < 14; i++) { Owner item = queue.Dequeue(); if (item.Id != i) return 5; }
                        for (nuint i = 0; i < 9; i++) if (input[i].Id != (int)i + 5) return 6;
                    }
                    for (int point = 1; point <= 9; point++)
                    {
                        Queue<Owner> queue = new();
                        queue.Enqueue(new Owner(1));
                        Vector<Owner> input = new();
                        for (int i = 0; i < 9; i++) input.Add(new Owner(i));
                        int baseline = live;
                        copies = 0; failCopy = point;
                        bool caught = false;
                        try { Apply(&queue, &input, form); }
                        catch (Exception* error) { caught = true; }
                        failCopy = 0;
                        if (!caught || live != baseline || owners != 10 || queue.Count != 1 || queue.Front.Id != 1) return 7;
                    }
                }
                if (owners != 0) return 8;
                return live;
            }
            """, optimization);

    private const string Tracking = """
        using Standard;
        using Standard::Collections;
        using Standard::Memory;
        using Standard::Queries;
        using Standard::Text;

        extern "C" void*? malloc(nuint bytes);
        extern "C" void free(void* storage);
        int live = 0;
        int attempts = 0;
        int failAt = 0;
        void*? latest = null;
        namespace Memory
        {
            public replace void*? Allocate(nuint bytes)
            {
                attempts++;
                if (attempts == failAt) return null;
                void*? result = malloc(bytes);
                if (result != null) live++;
                latest = result;
                return result;
            }
            public replace void Free(void* storage) nothrow { live--; free(storage); }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FreshMoveOnlyEnumerableValuesRemainUsableWithOptionalSpanOverloadPresent(int optimization)
        => Run(Tracking + """
            int owners = 0;
            int cursors = 0;
            int failure = 0;
            struct Owner
            {
                public int Id;
                public String Text;
                public Owner(int id) { Id = id; Text = s"owned"; owners++; }
                ~Owner() { owners--; }
            }
            struct Source
            {
                public Cursor GetEnumerator()
                {
                    if (failure == 1) throw new Exception("acquire");
                    return new();
                }
                public struct Cursor
                {
                    private int position;
                    private String token;
                    public Cursor() { position = 0; token = s"cursor"; cursors++; }
                    ~Cursor() { cursors--; }
                    public bool MoveNext()
                    {
                        position++;
                        if (failure == 2 && position == 3) throw new Exception("advance");
                        return position <= 6;
                    }
                    public Owner Current
                    {
                        get
                        {
                            if (failure == 3 && position == 3) throw new Exception("read");
                            return new Owner(7 - position);
                        }
                    }
                }
            }
            int main()
            {
                {
                    Queue<Owner> queue = new();
                    queue.Enqueue(new Owner(0));
                    nuint capacity = queue.Capacity;
                    int baseline = live;
                    var values = new Enumerable<Owner, Source, Source::Cursor>(new Source());
                    for (int mode = 1; mode <= 3; mode++)
                    {
                        failure = mode;
                        bool caught = false;
                        try { queue.EnqueueRange(values); } catch (Exception* error) { caught = true; }
                        failure = 0;
                        if (!caught || owners != 1 || cursors != 0 || live != baseline || queue.Count != 1 || queue.Capacity != capacity) return 1;
                    }
                    queue.EnqueueRange(values);
                    if (owners != 7 || cursors != 0 || queue.Count != 7) return 2;
                    for (int i = 0; i < 7; i++)
                    {
                        Owner item = queue.Dequeue();
                        if (i == 0 && item.Id != 0) return 3;
                        if (i != 0 && item.Id != 7 - i) return 3;
                    }
                    queue.EnqueueRange(values);
                    if (owners != 6 || cursors != 0 || queue.Count != 6) return 4;
                    for (int i = 1; i <= 6; i++)
                    {
                        Owner item = queue.Dequeue();
                        if (item.Id != 7 - i) return 5;
                    }
                }
                if (owners != 0 || cursors != 0) return 6;
                return live;
            }
            """, optimization);
}
