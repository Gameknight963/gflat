namespace gflat.Tests.Std.Collections;

public sealed class PriorityQueueRangeTests
{
    private static void Run(string code, int optimization)
    {
        var sources = StandardTestSources.Combine(StandardTestSources.Values, StandardTestSources.Load(
            "core/Collections/PriorityQueue.gf", "core/Collections/priority_queue_enqueue_range.gf",
            "core/Collections/priority_queue_queries.gf", "core/Queries/SelectSource.gf", "core/Queries/enumerable_select.gf"))
            .Append(new SourceFile("priority-queue-range.gf", code));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Priority range exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideCompiles() => StandardLibraryGuideTests.CheckProgram("PRIORITY_QUEUE_RANGE.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BulkResultsMatchDotNetAndEmptyHeapBuildsUseLinearComparisons(int optimization)
    {
        var random = new Random(771954);
        int[] values = Enumerable.Range(0, 64).Select(_ => random.Next(-100, 101)).ToArray();
        var expected = new List<int>();
        foreach (bool occupied in new[] { false, true })
        for (int count = 0; count <= values.Length; count++)
        {
            var queue = new PriorityQueue<int, int>();
            if (occupied) foreach (int item in new[] { -1000, 0, 1000 }) queue.Enqueue(item, item);
            queue.EnqueueRange(values.Take(count).Select(value => (value, value)));
            while (queue.TryDequeue(out int item, out _)) expected.Add(item);
        }
        Run(Tracking + $$"""
            int comparisons = 0;
            int Compare(immutable(int)* left, immutable(int)* right) { comparisons++; return *left - *right; }
            int Copy(immutable(int)* value) => *value;
            int main()
            {
                int[64] values = [{{string.Join(",", values)}}];
                int[{{expected.Count}}] expected = [{{string.Join(",", expected)}}];
                for (int form = 0; form < 2; form++)
                for (int reserved = 0; reserved < 2; reserved++)
                {
                    nuint at = 0;
                    for (int occupied = 0; occupied < 2; occupied++)
                    for (nuint count = 0; count <= 64; count++)
                    {
                        PriorityQueue<int> queue = new(Compare);
                        nuint originalBuffer = 0;
                        if (reserved != 0)
                        {
                            queue.Reserve(96);
                            originalBuffer = (latest!).reinterpret<nuint>();
                        }
                        nuint oldCount = 0;
                        if (occupied != 0)
                        {
                            queue.Enqueue(-1000); queue.Enqueue(0); queue.Enqueue(1000);
                            oldCount = 3;
                        }
                        comparisons = 0; attempts = 0;
                        if (form == 0) queue.EnqueueRange(new Span<immutable(int)>(&values[0], count));
                        else queue.EnqueueRange(new Span<int>(&values[0], count).AsEnumerable());
                        if (queue.Count != count + oldCount || queue.Capacity < queue.Count) return 1;
                        if (reserved != 0 && queue.Capacity < 96) return 9;
                        if (reserved != 0 && !queue.IsEmpty && (&queue.Top).reinterpret<nuint>() != originalBuffer) return 10;
                        if (occupied == 0 && (nuint)comparisons > 2 * count) return 2;
                        if (count == 0 && attempts != 0) return 3;
                        if (form == 0 && count != 0 && (occupied == 0 || reserved != 0) && attempts != 1) return 4;
                        while (!queue.IsEmpty) if (queue.Dequeue() != expected[at++]) return 5;
                    }
                    if (at != {{expected.Count}}) return 6;
                }
                {
                    PriorityQueue<int> queue = new(Compare);
                    queue.Enqueue(4); queue.Enqueue(1); queue.Enqueue(3); queue.Enqueue(2);
                    queue.EnqueueRange(new Span<immutable(int)>(&queue.Top, queue.Count));
                    var borrowed = queue.UnorderedItems.AsEnumerable().Select<int>(Copy);
                    queue.EnqueueRange(borrowed);
                    if (queue.Count != 16) return 7;
                    for (int i = 0; i < 16; i++) if (queue.Dequeue() != i / 4 + 1) return 8;
                }
                return live;
            }
            """, optimization);
    }

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
    public void ComparisonCopyAndAllocationFailuresPreserveHeapOwnershipAndCommittedPrefixes(int optimization)
        => Run(Tracking + """
            int owners = 0;
            int copies = 0;
            int failCopy = 0;
            int comparisons = 0;
            int failCompare = 0;
            struct Owner
            {
                public int Id;
                public String Text;
                public Owner(int id) { Id = id; Text = s"owned"; owners++; }
                public copy Owner(immutable(Owner)* source)
                {
                    copies++;
                    Id = source.Id; Text = source.Text;
                    if (copies == failCopy) throw new Exception("copy", 32);
                    owners++;
                }
                ~Owner() { owners--; }
            }
            int Compare(immutable(Owner)* a, immutable(Owner)* b)
            {
                comparisons++;
                if (comparisons == failCompare) throw new Exception("compare", 31);
                return a.Id - b.Id;
            }
            void Fill(PriorityQueue<Owner>* queue, bool occupied)
            {
                queue.Reserve(32);
                if (occupied) { queue.Enqueue(new Owner(10)); queue.Enqueue(new Owner(30)); queue.Enqueue(new Owner(50)); }
            }
            void Apply(PriorityQueue<Owner>* queue, Vector<Owner>* values, int form)
            {
                if (form == 0) queue.EnqueueRange(values.AsImmutable());
                else queue.EnqueueRange(values.AsEnumerable());
            }
            int Drain(PriorityQueue<Owner>* queue, bool occupied, nuint added)
            {
                int[9] wanted = [10, 30, 50, 40, 20, 60, 0, 45, 15];
                nuint beginning = 3;
                nuint count = added;
                if (occupied) { beginning = 0; count += 3; }
                for (nuint i = beginning; i < beginning + count; i++)
                for (nuint j = i + 1; j < beginning + count; j++)
                    if (wanted[i] > wanted[j]) { int temporary = wanted[i]; wanted[i] = wanted[j]; wanted[j] = temporary; }
                for (nuint i = 0; i < count; i++)
                {
                    Owner item = queue.Dequeue();
                    if (item.Id != wanted[beginning + i] || !item.Text.Equals("owned")) return 1;
                }
                if (!queue.IsEmpty) return 2;
                return 0;
            }
            int main()
            {
                {
                    Vector<Owner> values = new();
                    int[6] ids = [40, 20, 60, 0, 45, 15];
                    for (nuint i = 0; i < 6; i++) values.Add(new Owner(ids[i]));
                    for (int occupied = 0; occupied < 2; occupied++)
                    for (int form = 0; form < 2; form++)
                    {
                        int measuredComparisons = 0;
                        int measuredAllocations = 0;
                        {
                            PriorityQueue<Owner> queue = new(Compare);
                            Fill(&queue, occupied != 0);
                            comparisons = 0; attempts = 0;
                            Apply(&queue, &values, form);
                            measuredComparisons = comparisons;
                            measuredAllocations = attempts;
                        }
                        for (int kind = 0; kind < 3; kind++)
                        {
                            int points = measuredComparisons;
                            if (kind == 1) points = measuredAllocations;
                            if (kind == 2) points = 6;
                            for (int point = 1; point <= points; point++)
                            {
                                PriorityQueue<Owner> queue = new(Compare);
                                Fill(&queue, occupied != 0);
                                nuint oldCount = queue.Count;
                                nuint oldCapacity = queue.Capacity;
                                int baseline = live;
                                comparisons = 0; attempts = 0; copies = 0;
                                if (kind == 0) failCompare = point;
                                else if (kind == 1) failAt = point;
                                else failCopy = point;
                                bool caught = false;
                                try { Apply(&queue, &values, form); }
                                catch (Exception* error) { caught = true; }
                                failCompare = 0; failAt = 0; failCopy = 0;
                                if (!caught || queue.Capacity != oldCapacity) return 1;
                                nuint added = queue.Count - oldCount;
                                if ((kind != 0 || occupied == 0) && added != 0) return 2;
                                if (owners != (int)(values.Count + queue.Count) || live != baseline + (int)added) return 3;
                                int result = Drain(&queue, occupied != 0, added);
                                if (result != 0 || owners != 6) return 4;
                                for (nuint i = 0; i < 6; i++)
                                    if (values[i].Id != ids[i] || !values[i].Text.Equals("owned")) return 5;
                            }
                        }
                    }
                }
                if (owners != 0) return 6;
                return live;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FreshMoveOnlyEnumerableValuesRemainUsableWithOptionalSpanOverloadPresent(int optimization)
        => Run(Tracking + """
            int owners = 0;
            int cursors = 0;
            bool failRead = false;
            struct Owner
            {
                public int Id;
                public String Text;
                public Owner(int id) { Id = id; Text = s"owned"; owners++; }
                ~Owner() { owners--; }
            }
            struct Source
            {
                public Cursor GetEnumerator() => new();
                public struct Cursor
                {
                    private int position;
                    private String token;
                    public Cursor() { position = 0; token = s"cursor"; cursors++; }
                    ~Cursor() { cursors--; }
                    public bool MoveNext() { position++; return position <= 6; }
                    public Owner Current
                    {
                        get
                        {
                            if (failRead && position == 3) throw new Exception("read");
                            return new Owner(7 - position);
                        }
                    }
                }
            }
            int Compare(immutable(Owner)* a, immutable(Owner)* b) => a.Id - b.Id;
            int main()
            {
                {
                    PriorityQueue<Owner> queue = new(Compare);
                    queue.Enqueue(new Owner(0));
                    nuint capacity = queue.Capacity;
                    int baseline = live;
                    var values = new Enumerable<Owner, Source, Source::Cursor>(new Source());
                    failRead = true;
                    bool caught = false;
                    try { queue.EnqueueRange(values); } catch (Exception* error) { caught = true; }
                    failRead = false;
                    if (!caught || owners != 1 || cursors != 0 || live != baseline || queue.Count != 1 || queue.Capacity != capacity) return 1;
                    queue.EnqueueRange(values);
                    if (owners != 7 || cursors != 0 || queue.Count != 7) return 2;
                    for (int i = 0; i < 7; i++)
                    {
                        Owner item = queue.Dequeue();
                        if (item.Id != i) return 3;
                    }
                    queue.EnqueueRange(values);
                    if (owners != 6 || cursors != 0 || queue.Count != 6) return 4;
                    for (int i = 1; i <= 6; i++)
                    {
                        Owner item = queue.Dequeue();
                        if (item.Id != i) return 5;
                    }
                }
                if (owners != 0 || cursors != 0) return 6;
                return live;
            }
            """, optimization);
}
