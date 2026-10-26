namespace gflat.Tests;

public sealed class StandardQueueStackTests
{
    private static IEnumerable<SourceFile> Sources(string program) => new[] {
        "Collections/IEnumerable.gf", "Queries/Enumerable.gf", "IStringConvertable.gf",
        "IHashable.gf", "IEquatable.gf", "IComparable.gf", "Hashing/primitive_hashing.gf", "String.gf", "Text/ByteSearch.gf",
        "Collections/Vector.gf", "Memory/Span.gf", "Collections/Stack.gf", "Collections/Queue.gf"
    }.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
        .Append(new SourceFile("main.gf", program));

    [Fact]
    public void QueueWrapsGrowsAndEnumeratesInFifoOrder()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Memory; using std::Queries; using std::Collections;
            int main() {
                Queue<int> items = new();
                items.Reserve(4);
                for (int i = 0; i < 4; i++) items.Enqueue(i);
                if (items.Dequeue() != 0 || items.Dequeue() != 1) return 1;
                items.Enqueue(4); items.Enqueue(5);
                int expected = 2;
                foreach (int item in items) { if (item != expected) return 2; expected++; }
                if (expected != 6) return 3;
                items.Enqueue(6);
                items.Front = 20;
                if (items.Dequeue() != 20) return 4;
                for (int i = 3; i < 7; i++) if (items.Dequeue() != i) return 5;
                if (!items.IsEmpty) return 6;
                for (int i = 0; i < 1000; i++) {
                    items.Enqueue(i);
                    if (items.Dequeue() != i) return 7;
                }
                expected = 0;
                for (int i = 0; i < 2000; i++) {
                    items.Enqueue(i);
                    if (items.Count > 17 && items.Dequeue() != expected++) return 8;
                    if (i % 57 == 0) items.Reserve(items.Capacity + 1);
                }
                while (!items.IsEmpty) if (items.Dequeue() != expected++) return 9;
                if (expected != 2000) return 10;
                items.Clear(); items.Enqueue(42);
                return items.Dequeue() - 42;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void StackEnumeratesAndPopsFromTheTop()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Memory; using std::Queries; using std::Collections;
            int main() {
                Stack<int> items = new(); items.Reserve(100);
                for (int i = 0; i < 100; i++) items.Push(i);
                int expected = 99;
                foreach (int item in items) { if (item != expected) return 1; expected--; }
                if (expected != -1 || items.Count != 100) return 2;
                items.Top = 200;
                if (items.Pop() != 200) return 3;
                for (int i = 98; i >= 0; i--) if (items.Pop() != i) return 4;
                if (!items.IsEmpty) return 5;
                items.Push(1); items.Clear(); items.Push(7);
                return items.Pop() - 7;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void StringsAreCopiedOnInsertionAndTransferredOnRemoval()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Memory; using std::Queries; using std::Collections;
            int main() {
                String original = s"original";
                Queue<String> queue = new(); Stack<String> stack = new();
                queue.Enqueue(original); stack.Push(original);
                original[0] = 'X';
                for (int i = 0; i < 20; i++) { queue.Enqueue(s"queued"); stack.Push(s"stacked"); }
                if (queue.Front.Data[0] != 'o') return 1;
                String first = queue.Dequeue();
                if (first.Length != 8 || first.Data[0] != 'o') return 2;
                queue.Front.Append("!", 1);
                if (queue.Dequeue().Length != 7) return 3;
                for (int i = 0; i < 20; i++) { String value = stack.Pop(); if (value.Length != 7) return 4; }
                String bottom = stack.Pop();
                if (bottom.Data[0] != 'o') return 5;
                Queue<String> moved = move queue;
                if (moved.Count != 19) return 6;
                queue = new(); queue.Enqueue(s"reused");
                stack.Push(s"reused");
                return 0;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void EachOwnedElementIsDestroyedExactlyOnce()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Memory; using std::Queries; using std::Collections;
            struct Item {
                private int* destroyed;
                public Item(int* counter) { destroyed = counter; }
                ~Item() { (*destroyed)++; }
            }
            int main() {
                int destroyed = 0;
                {
                    Queue<Item> queue = new(); Stack<Item> stack = new();
                    for (int i = 0; i < 20; i++) { queue.Enqueue(new Item(&destroyed)); stack.Push(new Item(&destroyed)); }
                    { Item first = queue.Dequeue(); Item top = stack.Pop(); }
                    if (destroyed != 2) return 1;
                    queue.Clear();
                    if (destroyed != 21) return 2;
                    queue.Enqueue(new Item(&destroyed));
                }
                return destroyed - 41;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void RawPointerElementsNeverTransferPointeeDestructionToContainers()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Memory; using std::Queries; using std::Collections;
            struct Resource {
                private int* destroyed;
                public Resource(int* counter) { destroyed = counter; }
                ~Resource() { (*destroyed)++; }
            }
            int main() {
                int destroyed = 0;
                Resource* first = new* Resource(&destroyed);
                Resource* second = new* Resource(&destroyed);
                {
                    Queue<Resource*> queue = new(); Stack<Resource*> stack = new();
                    queue.Enqueue(first); queue.Enqueue(second);
                    stack.Push(first); stack.Push(second);
                    queue.Clear();
                    if (destroyed != 0) return 1;
                }
                if (destroyed != 0) return 2;
                delete first; delete second;
                return destroyed - 2;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ZeroSizedElementsAndCapacityOverflowRemainWellDefined()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Memory; using std::Queries; using std::Collections;
            struct Empty {}
            int main() {
                Queue<Empty> queue = new(); Stack<Empty> stack = new();
                for (int i = 0; i < 30; i++) { queue.Enqueue(new Empty()); stack.Push(new Empty()); }
                for (int i = 0; i < 15; i++) { queue.Dequeue(); stack.Pop(); }
                for (int i = 0; i < 30; i++) { queue.Enqueue(new Empty()); stack.Push(new Empty()); }
                if (queue.Count != 45 || stack.Count != 45) return 1;
                queue.Clear(); stack.Clear();
                Queue<int> ints = new(); ints.Enqueue(42);
                try { ints.Reserve((nuint)0 - (nuint)1); return 2; } catch (ref Exception error) {}
                if (ints.Count != 1 || ints.Dequeue() != 42) return 3;
                return 0;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void TryRemovalPreservesEmptyOutputsAndReplacesOwningValues()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Memory; using std::Queries; using std::Collections;
            int main() {
                Queue<String> queue = new(); Stack<String> stack = new();
                String output = s"unchanged";
                if (queue.TryDequeue(ref output) || stack.TryPop(ref output) || output.Length != 9) return 1;
                queue.Enqueue(s"queue"); stack.Push(s"stacked");
                if (!queue.TryDequeue(ref output) || output.Length != 5 || !queue.IsEmpty) return 2;
                if (!stack.TryPop(ref output) || output.Length != 7 || !stack.IsEmpty) return 3;
                if (queue.TryDequeue(ref output) || output.Length != 7) return 4;
                return 0;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void FailedQueueGrowthPreservesExistingElementsAndDestroysArgument()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Memory; using std::Queries; using std::Collections;
            extern void* malloc(nuint bytes);
            extern void free(void* address);
            namespace Memory {
                replace void*? Allocate(nuint bytes) { if (bytes > 64) return null; return malloc(bytes); }
                replace void Free(void* address) nothrow { free(address); }
            }
            struct Item {
                private int* destroyed;
                public int Number;
                public Item(int* counter, int number) { destroyed = counter; Number = number; }
                ~Item() { (*destroyed)++; }
            }
            int main() {
                int destroyed = 0;
                {
                    Queue<Item> items = new();
                    for (int i = 0; i < 4; i++) items.Enqueue(new Item(&destroyed, i));
                    try { items.Enqueue(new Item(&destroyed, 4)); return 1; }
                    catch (ref BadAllocException error) {}
                    if (items.Count != 4 || destroyed != 1) return 2;
                    for (int i = 0; i < 4; i++) { Item item = items.Dequeue(); if (item.Number != i) return 3; }
                }
                return destroyed - 5;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void EmptyOperationsAndUnpositionedEnumeratorsThrow()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Memory; using std::Queries; using std::Collections;
            int main() {
                Queue<int> queue = new(); Stack<int> stack = new(); int caught = 0;
                try { queue.Dequeue(); } catch (ref Exception error) { caught++; }
                try { stack.Pop(); } catch (ref Exception error) { caught++; }
                try { int ignored = queue.Front; } catch (ref Exception error) { caught++; }
                try { int ignored = stack.Top; } catch (ref Exception error) { caught++; }
                Queue<int>::Enumerator qe = queue.GetEnumerator();
                Stack<int>::Enumerator se = stack.GetEnumerator();
                try { int ignored = qe.Current; } catch (ref Exception error) { caught++; }
                try { int ignored = se.Current; } catch (ref Exception error) { caught++; }
                if (qe.MoveNext() || se.MoveNext() || qe.MoveNext() || se.MoveNext()) return 1;
                queue.Enqueue(42); stack.Push(42);
                qe = queue.GetEnumerator(); se = stack.GetEnumerator();
                if (!qe.MoveNext() || !se.MoveNext() || qe.Current != 42 || se.Current != 42) return 2;
                if (qe.MoveNext() || se.MoveNext()) return 3;
                try { int ignored = qe.Current; } catch (ref Exception error) { caught++; }
                try { int ignored = se.Current; } catch (ref Exception error) { caught++; }
                return caught - 8;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }
}
