using gflat.CompileExceptions;

namespace gflat.Tests.Std.Collections;

public sealed class PriorityQueueItemTests
{
    private static IEnumerable<SourceFile> Sources(string code) => StandardTestSources.Combine(StandardTestSources.Values,
        StandardTestSources.Load("core/Collections/PriorityQueue.gf", "core/Collections/priority_queue_queries.gf",
            "core/Queries/WhereSource.gf", "core/Queries/SelectSource.gf", "core/Queries/enumerable_where.gf",
            "core/Queries/enumerable_select.gf", "core/Queries/enumerable_count.gf", "core/Queries/enumerable_to_vector.gf"))
        .Append(new SourceFile("priority-queue-items.gf", code));

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("PRIORITY_QUEUE_ITEMS.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ViewsExposeEveryItemAndFreshIterationsSeeTheCurrentQueue(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard;
            using Standard::Collections;
            using Standard::Queries;

            bool denyCompare = false;
            int Compare(immutable(int)* left, immutable(int)* right)
            {
                if (denyCompare) throw new Exception("unexpected comparison");
                if (*left < *right) return -1;
                if (*left > *right) return 1;
                return 0;
            }
            bool Positive(immutable(int)* value) => *value > 0;
            int Read(immutable(int)* value) => *value;
            nuint Count(immutable(PriorityQueue<int>)* source) => source.UnorderedItems.Count;
            int main()
            {
                PriorityQueue<int> queue = new(Compare);
                var items = queue.UnorderedItems;
                var query = items.AsEnumerable().Where(Positive);
                for (int length = 0; length <= 50; length++)
                {
                    queue.Clear();
                    int[31] expected = default(int[31]);
                    nuint positives = 0;
                    for (int i = 0; i < length; i++)
                    {
                        int value = i * 17 % 31 - 15;
                        expected[(nuint)(value + 15)]++;
                        if (value > 0) positives++;
                        queue.Enqueue(value);
                    }
                    denyCompare = true;
                    if (items.Count != (nuint)length || Count(&queue) != (nuint)length || items.IsEmpty != (length == 0)) return 1;
                    nuint seen = 0;
                    foreach (immutable(int)* value in items)
                    {
                        if (*value < -15 || *value > 15) return 2;
                        expected[(nuint)(*value + 15)]--;
                        seen++;
                    }
                    if (seen != (nuint)length || query.Count() != positives) return 3;
                    for (nuint i = 0; i < 31; i++) if (expected[i] != 0) return 4;
                    Vector<int> selected = queue.UnorderedItems.AsEnumerable().Where(Positive).Select<int>(Read).ToVector();
                    if (selected.Count != positives) return 5;
                    foreach (int value in selected) if (value <= 0) return 6;
                    denyCompare = false;
                }
                queue.Reserve(256);
                queue.Clear();
                queue.Enqueue(7);
                if (items.Count != 1 || query.Count() != 1) return 7;
                var iterator = items.GetEnumerator();
                bool caught = false;
                try { iterator.Current; }
                catch (Exception* error) { caught = true; }
                if (!caught || !iterator.MoveNext() || *iterator.Current != 7 || iterator.MoveNext() || iterator.MoveNext()) return 8;
                caught = false;
                try { iterator.Current; }
                catch (Exception* error) { caught = true; }
                if (!caught || queue.Dequeue() != 7 || !items.IsEmpty || query.Count() != 0) return 9;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Priority queue item view exit {result.ExitCode}: {result.StandardError}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void NoncopyableItemsAreBorrowedWithoutAllocationsOrComparisons(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard;
            using Standard::Collections;
            using Standard::Queries;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int attempts = 0;
            int owners = 0;
            bool deny = false;
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
                public Owner(int value) { Value = value; owners++; }
                ~Owner() { owners--; }
            }
            int Compare(immutable(Owner)* left, immutable(Owner)* right)
            {
                if (deny) throw new Exception("unexpected comparison");
                if (left.Value < right.Value) return -1;
                if (left.Value > right.Value) return 1;
                return 0;
            }
            bool All(immutable(Owner)* value) => value.Value >= 0;
            int main()
            {
                {
                    PriorityQueue<Owner> queue = new(Compare);
                    for (int i = 0; i < 8; i++) queue.Enqueue(new Owner(i));
                    immutable(Owner)* first = &queue.Top;
                    int before = attempts;
                    deny = true;
                    bool found = false;
                    int sum = 0;
                    foreach (immutable(Owner)* value in queue.UnorderedItems)
                    {
                        if (value == first) found = true;
                        sum += value.Value;
                    }
                    if (!found || sum != 28 || queue.UnorderedItems.AsEnumerable().Where(All).Count() != 8) return 1;
                    if (owners != 8 || attempts != before) return 2;
                    deny = false;
                }
                if (owners != 0) return 3;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Priority queue owning view exit {result.ExitCode}: {result.StandardError}");
    }

    [Fact]
    public void ItemsCannotBeModifiedThroughTheView()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources("""
            using Standard::Collections;

            struct Item { public int Value; }
            int Compare(immutable(Item)* left, immutable(Item)* right) => 0;
            int main()
            {
                PriorityQueue<Item> queue = new(Compare);
                var iterator = queue.UnorderedItems.GetEnumerator();
                iterator.MoveNext();
                iterator.Current.Value = 10;
                return 0;
            }
            """)));
        Assert.Contains("readonly", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
