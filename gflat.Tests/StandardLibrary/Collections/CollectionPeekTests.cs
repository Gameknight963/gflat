namespace gflat.Tests.Std.Collections;

public sealed partial class CollectionPeekTests
{
    private static void Run(string program, int optimization)
    {
        var sources = StandardTestSources.Combine(StandardTestSources.Values, StandardTestSources.Load(
            "core/Collections/Queue.gf", "core/Collections/Stack.gf", "core/Collections/PriorityQueue.gf",
            "core/Collections/queue_peek.gf", "core/Collections/stack_peek.gf", "core/Collections/priority_queue_peek.gf"))
            .Append(new SourceFile("collection-peek-tests.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Collection peek exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("COLLECTION_PEEK.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OptionalPeekFilesDoNotRequireCopiesForBorrowedContainerOperations(int optimization)
        => Run("""
            using Standard;
            using Standard::Collections;
            using Standard::Text;

            int live = 0;
            struct Owner
            {
                public int Id;
                private String text;
                public Owner(int id) { Id = id; text = s"owned"; live++; }
                ~Owner() { live--; }
            }
            int Compare(immutable(Owner)* left, immutable(Owner)* right) => left.Id - right.Id;
            int main()
            {
                {
                    Queue<Owner> queue = new();
                    Stack<Owner> stack = new();
                    PriorityQueue<Owner> priority = new(Compare);
                    queue.Enqueue(new Owner(1));
                    stack.Push(new Owner(2));
                    priority.Enqueue(new Owner(3));
                    if (queue.Front.Id != 1 || stack.Top.Id != 2 || priority.Top.Id != 3) return 1;
                    Owner first = queue.Dequeue();
                    Owner top = stack.Pop();
                    Owner minimum = priority.Dequeue();
                    if (first.Id != 1 || top.Id != 2 || minimum.Id != 3 || live != 3) return 2;
                }
                return live;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MixedOperationsMatchDotNetAndEmptyOutputsArePreserved(int optimization)
    {
        var queue = new Queue<int>();
        var stack = new Stack<int>();
        var priority = new PriorityQueue<int, int>();
        var random = new Random(927481);
        string path = Path.Combine(Path.GetTempPath(), "gflat_collection_peek_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            for (int step = 0; step < 512; step++)
            {
                int operation = random.Next(12);
                if (step < 20) operation = 0;
                int value = random.Next(-1000, 1001);
                if (operation < 5) { queue.Enqueue(value); stack.Push(value); priority.Enqueue(value, value); }
                else if (operation < 9 && queue.Count != 0) { queue.Dequeue(); stack.Pop(); priority.Dequeue(); }
                else if (operation == 11) { queue.Clear(); stack.Clear(); priority.Clear(); }
                writer.Write(operation); writer.Write(value); writer.Write(queue.Count);
                writer.Write(queue.TryPeek(out int q) ? q : 77);
                writer.Write(stack.TryPeek(out int s) ? s : 77);
                writer.Write(priority.TryPeek(out int p, out _) ? p : 77);
            }
            string literal = "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            Run($$"""
                using Standard;
                using Standard::Collections;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int ferror(void* file);
                extern "C" int fclose(void* file);
                int Compare(immutable(int)* left, immutable(int)* right) => *left - *right;
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 1;
                    defer { fclose(file!); }
                    Queue<int> queue = new();
                    Stack<int> stack = new();
                    PriorityQueue<int> priority = new(Compare);
                    int[6] row = default(int[6]);
                    while (true)
                    {
                        nuint read = fread(&row[0], 1, 24, file!);
                        if (read == 0) break;
                        if (read != 24) return 2;
                        if (row[0] < 5) { queue.Enqueue(row[1]); stack.Push(row[1]); priority.Enqueue(row[1]); }
                        else if (row[0] < 9 && !queue.IsEmpty) { queue.Dequeue(); stack.Pop(); priority.Dequeue(); }
                        else if (row[0] == 11) { queue.Clear(); stack.Clear(); priority.Clear(); }
                        bool expected = row[2] != 0;
                        int q = 77;
                        int s = 77;
                        int p = 77;
                        immutable(Queue<int>)* qView = &queue;
                        immutable(Stack<int>)* sView = &stack;
                        immutable(PriorityQueue<int>)* pView = &priority;
                        if (qView.TryPeek(ref q) != expected || sView.TryPeek(ref s) != expected || pView.TryPeek(ref p) != expected) return 3;
                        if (q != row[3] || s != row[4] || p != row[5]) return 4;
                        q = 77; s = 77; p = 77;
                        if (TryPeek(qView, ref q) != expected || TryPeek(sView, ref s) != expected || TryPeek(pView, ref p) != expected) return 5;
                        if (q != row[3] || s != row[4] || p != row[5]) return 6;
                        if (queue.Count != (nuint)row[2] || stack.Count != (nuint)row[2] || priority.Count != (nuint)row[2]) return 7;
                        if (expected)
                        {
                            if (!queue.TryPeek(ref queue.Front) || !stack.TryPeek(ref stack.Top)) return 8;
                            if (queue.Front != q || stack.Top != s) return 9;
                        }
                    }
                    if (ferror(file!) != 0) return 10;
                    return 0;
                }
                """, optimization);
        }
        finally { File.Delete(path); }
    }
}
