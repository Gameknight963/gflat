namespace gflat.Tests.Std.Collections;

public sealed class QueueStackContainsTests
{
    private static void Run(string program, int optimization)
    {
        var sources = StandardTestSources.Combine(StandardTestSources.Values, StandardTestSources.Load(
            "core/Collections/Queue.gf", "core/Collections/Stack.gf", "core/Collections/EqualityComparer.gf",
            "core/Collections/queue_search.gf", "core/Collections/stack_search.gf"))
            .Append(new SourceFile("queue-stack-contains.gf", program));
        var result = CompilerTestHelper.Run(sources, optimization);
        Assert.True(result.ExitCode == 0, $"Contains exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
    }

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("QUEUE_STACK_CONTAINS.md", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CustomEqualityNeedsNoEquatableContractAndCanBorrowTheKey(int optimization)
        => Run("""
            using Standard;
            using Standard::Collections;

            struct Plain
            {
                public int Id;
                public Plain(int id) { Id = id; }
                ~Plain() { }
            }
            bool Equal(immutable(Plain)* left, immutable(Plain)* right) => left.Id == right.Id;
            bool FindQueue(immutable(Queue<Plain>)* source, immutable(Plain)* key) => source.Contains(key, Equal);
            bool FindStack(immutable(Stack<Plain>)* source, immutable(Plain)* key) => source.Contains(key, Equal);
            int main()
            {
                Queue<Plain> queue = new();
                Stack<Plain> stack = new();
                queue.Enqueue(new Plain(7)); stack.Push(new Plain(8));
                if (!FindQueue(&queue, &queue.Front) || !FindStack(&stack, &stack.Top)) return 1;
                Plain absent = new(9);
                if (queue.Contains(&absent, Equal) || stack.Contains(&absent, Equal)) return 2;
                return 0;
            }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void WrappedAndContiguousSearchesMatchDotNet(int optimization)
    {
        var random = new Random(194728);
        string path = Path.Combine(Path.GetTempPath(), "gflat_contains_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            for (int scenario = 0; scenario < 128; scenario++)
            {
                var queue = new Queue<int>();
                var stack = new Stack<int>();
                int rotations = scenario % 17;
                writer.Write(rotations);
                for (int i = 0; i < 16; i++)
                {
                    int value = random.Next(-8, 9);
                    queue.Enqueue(value); stack.Push(value); writer.Write(value);
                }
                for (int i = 0; i < rotations; i++) { queue.Dequeue(); stack.Pop(); }
                for (int i = 0; i < rotations; i++)
                {
                    int value = random.Next(-8, 9);
                    queue.Enqueue(value); stack.Push(value); writer.Write(value);
                }
                for (int key = -10; key <= 10; key++)
                {
                    writer.Write(queue.Contains(key) ? 1 : 0);
                    writer.Write(stack.Contains(key) ? 1 : 0);
                }
                foreach (int value in queue) writer.Write(value);
                foreach (int value in stack) writer.Write(value);
            }
            string literal = "\"" + path.Replace("\\", "\\\\") + "\"";
            Run($$"""
                using Standard;
                using Standard::Collections;

                extern "C" void*? fopen(readonly(char)* path, readonly(char)* mode);
                extern "C" nuint fread(void* buffer, nuint size, nuint count, void* file);
                extern "C" int fclose(void* file);
                int Read(void* file)
                {
                    int value = 0;
                    if (fread(&value, 4, 1, file) != 1) throw new Exception("Incomplete fixture");
                    return value;
                }
                int main()
                {
                    void*? file = fopen({{literal}}, "rb");
                    if (file == null) return 10;
                    defer { fclose(file!); }
                    for (int scenario = 0; scenario < 128; scenario++)
                    {
                        Queue<int> queue = new();
                        Stack<int> stack = new();
                        queue.Reserve(16); stack.Reserve(16);
                        int rotations = Read(file!);
                        for (int i = 0; i < 16; i++)
                        {
                            int value = Read(file!); queue.Enqueue(value); stack.Push(value);
                        }
                        for (int i = 0; i < rotations; i++) { int removed = queue.Dequeue(); int popped = stack.Pop(); }
                        for (int i = 0; i < rotations; i++)
                        {
                            int value = Read(file!); queue.Enqueue(value); stack.Push(value);
                        }
                        for (int key = -10; key <= 10; key++)
                        {
                            bool queueExpected = Read(file!) == 1;
                            bool stackExpected = Read(file!) == 1;
                            if (queue.Contains(&key) != queueExpected || Contains(&stack, &key) != stackExpected) return 1;
                        }
                        for (int i = 0; i < 16; i++) { if (queue.Dequeue() != Read(file!)) return 2; }
                        for (int i = 0; i < 16; i++) { if (stack.Pop() != Read(file!)) return 3; }
                        int absent = 0;
                        if (queue.Contains(&absent) || stack.Contains(&absent)) return 4;
                    }
                    return 0;
                }
                """, optimization);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SearchesBorrowNoncopyableOwnersAndShortCircuitInEnumerationOrder(int optimization)
        => Run("""
            using Standard;
            using Standard::Collections;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int attempts = 0;
            int live = 0;
            int calls = 0;
            int order = 0;
            int throwAt = 0;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes) { attempts++; return malloc(bytes); }
                public replace void Free(void* storage) nothrow { free(storage); }
            }
            struct Owner : IEquatable<Owner>
            {
                public int Id;
                public Owner(int id) { Id = id; live++; }
                public immutable bool Equals(immutable(Owner)* other) => Id == other.Id;
                ~Owner() { live--; }
            }
            bool Equal(immutable(Owner)* left, immutable(Owner)* right)
            {
                calls++;
                order = order * 10 + left.Id;
                if (calls == throwAt) throw new Exception("comparison failed");
                return left.Id == right.Id;
            }
            int main()
            {
                {
                    Queue<Owner> queue = new();
                    Stack<Owner> stack = new();
                    queue.Reserve(4); stack.Reserve(4);
                    for (int i = 1; i <= 4; i++) { queue.Enqueue(new Owner(i)); stack.Push(new Owner(i)); }
                    { Owner removed = queue.Dequeue(); }
                    queue.Enqueue(new Owner(5));
                    Owner key = new(3);
                    int before = attempts;
                    if (!queue.Contains(&key) || !stack.Contains(&key)) return 1;
                    if (!queue.Contains(&key, Equal) || calls != 2 || order != 23) return 2;
                    calls = 0; order = 0;
                    if (!Contains(&stack, &key, Equal) || calls != 2 || order != 43) return 3;
                    if (attempts != before || live != 9) return 4;
                    for (int failure = 1; failure <= 2; failure++)
                    {
                        calls = 0; order = 0; throwAt = failure;
                        bool caught = false;
                        try { queue.Contains(&key, Equal); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || calls != failure || queue.Count != 4 || queue.Front.Id != 2 || live != 9) return 5;
                        calls = 0; order = 0;
                        caught = false;
                        try { stack.Contains(&key, Equal); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || calls != failure || stack.Count != 4 || stack.Top.Id != 4 || live != 9) return 6;
                    }
                    throwAt = 0;
                    for (int i = 2; i <= 5; i++) { Owner value = queue.Dequeue(); if (value.Id != i) return 7; }
                    for (int i = 4; i >= 1; i--) { Owner value = stack.Pop(); if (value.Id != i) return 8; }
                    calls = 0;
                    if (queue.Contains(&key, Equal) || stack.Contains(&key, Equal) || calls != 0) return 9;
                }
                return live;
            }
            """, optimization);
}
