namespace gflat.Tests.Std.Collections;

public sealed class QueueStackCopyTests
{
    private static IEnumerable<SourceFile> Sources(string code) => StandardTestSources.Combine(StandardTestSources.Values,
        StandardTestSources.Load("core/Collections/LinkedList.gf", "core/Collections/linked_list_copy.gf",
            "core/Collections/Deque.gf", "core/Collections/deque_copy.gf", "core/Collections/Queue.gf", "core/Collections/Stack.gf",
            "core/Collections/queue_copy.gf", "core/Collections/stack_copy.gf"))
        .Append(new SourceFile("queue-stack-copy.gf", code));

    [Fact]
    public void GuideExampleCompiles() => StandardLibraryGuideTests.CheckProgram("QUEUE_STACK_COPY.md", 0);

    [Fact]
    public void LinkedListGuideCompiles() => StandardLibraryGuideTests.CheckProgram("LINKED_LIST_COPY.md", 0);

    [Theory]
    [InlineData("LinkedList", "AddLast", "RemoveFirst", "First!.Value", 0)]
    [InlineData("LinkedList", "AddLast", "RemoveFirst", "First!.Value", 2)]
    [InlineData("Deque", "AddLast", "RemoveFirst", "Front", 0)]
    [InlineData("Deque", "AddLast", "RemoveFirst", "Front", 2)]
    [InlineData("Queue", "Enqueue", "Dequeue", "Front", 0)]
    [InlineData("Queue", "Enqueue", "Dequeue", "Front", 2)]
    [InlineData("Stack", "Push", "Pop", "Top", 0)]
    [InlineData("Stack", "Push", "Pop", "Top", 2)]
    public void OrderingBoundsAndOverlapsPreserveSources(string type, string add, string remove, string peek, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources($$"""
            using Standard;
            using Standard::Collections;
            using Standard::Memory;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            bool deny = false;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    if (deny) return null;
                    return malloc(bytes);
                }
                public replace void Free(void* storage) nothrow => free(storage);
            }
            void Copy(immutable({{type}}<int>)* source, Span<int> output, nuint index)
                => source.CopyTo(output, index);
            int main()
            {
                for (nuint size = 0; size <= 12; size++)
                {
                    {{type}}<int> source = new();
                    {{(type == "LinkedList" ? "" : "source.Reserve(16);")}}
                    for (nuint i = 0; i < size; i++) source.{{add}}((int)i);
                    {{(type != "Stack" ? $"if (size != 0) for (int i = 0; i < 13; i++) {{ int item = source.{remove}(); source.{add}(item); }}" : "")}}
                    int[16] buffer = default(int[16]);
                    Span<int> output = new(&buffer[0], 16);
                    for (nuint offset = 0; offset <= 16; offset++)
                    {
                        for (nuint i = 0; i < 16; i++) output[i] = -7;
                        bool fits = size <= 16 - offset;
                        deny = true;
                        bool copied = Standard::Collections::TryCopyTo<int>(&source, output.Slice(offset));
                        deny = false;
                        if (copied != fits) return 1;
                        for (nuint i = 0; i < 16; i++)
                        {
                            int expected = -7;
                            if (fits && i >= offset && i - offset < size)
                            {
                                expected = (int)(i - offset);
                                {{(type == "Stack" ? "expected = (int)(size - 1) - expected;" : "expected = (expected + 13) % (int)size;")}}
                            }
                            if (output[i] != expected) return 2;
                        }
                        bool caught = false;
                        try { Copy(&source, output, offset); }
                        catch (Exception* error) { caught = true; }
                        if (caught == fits || source.Count != size) return 3;
                    }
                    bool invalid = false;
                    try { Copy(&source, output, 17); }
                    catch (Exception* error) { invalid = true; }
                    if (!invalid) return 4;
                    if (size != 0)
                    {
                        bool overlap = false;
                        try { source.CopyTo(new Span<int>(&source.{{peek}}, size)); }
                        catch (Exception* error) { overlap = true; }
                        if (!overlap) return 5;
                        {{(type == "LinkedList" ? """
                        LinkedList<int>::Node*? node = source.First;
                        while (node != null)
                        {
                            bool caughtOverlap = false;
                            try { source.CopyTo(new Span<int>(&node!.Value, size)); }
                            catch (Exception* error) { caughtOverlap = true; }
                            if (!caughtOverlap) return 8;
                            node = node!.Next;
                        }
                        """ : "")}}

                        for (nuint i = 0; i < size; i++)
                        {
                            int expected = (int)i;
                            {{(type == "Stack" ? "expected = (int)(size - 1) - expected;" : "expected = (expected + 13) % (int)size;")}}
                            if (source.{{remove}}() != expected) return 6;
                        }
                    }
                }
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}\n{result.StandardError}");
    }

    [Theory]
    [InlineData("LinkedList", "AddLast", 0)]
    [InlineData("LinkedList", "AddLast", 2)]
    [InlineData("Deque", "AddLast", 0)]
    [InlineData("Deque", "AddLast", 2)]
    [InlineData("Queue", "Enqueue", 0)]
    [InlineData("Queue", "Enqueue", 2)]
    [InlineData("Stack", "Push", 0)]
    [InlineData("Stack", "Push", 2)]
    public void CopyFailuresKeepCompletedPrefixAndCleanFailedOwners(string type, string add, int optimization)
    {
        var result = CompilerTestHelper.Run(Sources($$"""
            using Standard;
            using Standard::Collections;
            using Standard::Memory;
            using Standard::Text;

            extern "C" void*? malloc(nuint bytes);
            extern "C" void free(void* storage);
            int live = 0;
            int copies = 0;
            int failAt = -1;
            namespace Memory
            {
                public replace void*? Allocate(nuint bytes)
                {
                    void*? value = malloc(bytes);
                    if (value != null) live++;
                    return value;
                }
                public replace void Free(void* storage) nothrow { live--; free(storage); }
            }
            struct Item
            {
                public int Key;
                public String Text;
                public Item(int key) { Key = key; Text = s"owned"; }
                public copy Item(immutable(Item)* source)
                {
                    copies++;
                    Key = source.Key;
                    Text = source.Text;
                    if (copies == failAt) throw new Exception("copy failed");
                }
            }
            int main()
            {
                {
                    {{type}}<Item> source = new();
                    for (int i = 0; i < 3; i++) source.{{add}}(new Item(i));
                    {
                        Vector<Item> shortTarget = new();
                        shortTarget.Add(new Item(99));
                        copies = 0;
                        failAt = 1;
                        if (source.TryCopyTo(shortTarget.AsSpan()) || copies != 0 || shortTarget[0].Key != 99) return 5;
                        failAt = -1;
                    }
                    for (int failure = 1; failure <= 3; failure++)
                    {
                        Vector<Item> target = new();
                        for (int i = 0; i < 4; i++) target.Add(new Item(99));
                        int before = live;
                        copies = 0;
                        failAt = failure;
                        bool caught = false;
                        try { source.CopyTo(target.AsSpan()); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || live != before || copies != failure || source.Count != 3) return 1;
                        for (nuint i = 0; i < 4; i++)
                        {
                            int expected = 99;
                            if (i < (nuint)(failure - 1)) expected = {{(type != "Stack" ? "(int)i" : "2 - (int)i")}};
                            if (target[i].Key != expected || !target[i].Text.Equals("owned")) return 2;
                        }
                        failAt = -1;
                        source.CopyTo(target.AsSpan());
                        for (nuint i = 0; i < 3; i++)
                            if (target[i].Key != {{(type != "Stack" ? "(int)i" : "2 - (int)i")}}) return 3;
                    }
                }
                if (live != 0) return 4;
                return 0;
            }
            """), optimization);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}\n{result.StandardError}");
    }

    [Fact]
    public void LoadingCopyExtensionsAllowsNoncopyableElements()
        => Compiler.Emit(Sources("""
            using Standard::Collections;

            struct Owner
            {
                public Owner() { }
                ~Owner() { }
            }
            int main()
            {
                LinkedList<Owner> list = new();
                list.AddLast(new Owner());
                list.RemoveFirst();
                Deque<Owner> deque = new();
                deque.AddFirst(new Owner());
                deque.RemoveLast();
                Queue<Owner> queue = new();
                Stack<Owner> stack = new();
                queue.Enqueue(new Owner());
                stack.Push(new Owner());
                queue.Dequeue();
                stack.Pop();
                return 0;
            }
            """));
}
