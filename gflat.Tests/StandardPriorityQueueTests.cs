namespace gflat.Tests;

public sealed class StandardPriorityQueueTests
{
    private static IEnumerable<SourceFile> Sources(string program) => new[] {
        "Collections/IEnumerable.gf", "Queries/Enumerable.gf", "IStringConvertable.gf", "IHashable.gf",
        "IEquatable.gf", "IComparable.gf", "Hashing/primitive_hashing.gf", "String.gf", "ByteSearch.gf", "Collections/Vector.gf", "Memory/Span.gf", "Collections/PriorityQueue.gf"
    }.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
        .Append(new SourceFile("main.gf", program));

    [Fact]
    public void HeapOrdersDuplicatesAndAlternatingInsertionsAndRemovals()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Memory; using std::Queries; using std::Collections;
            int Compare(readonly(int)* left, readonly(int)* right) {
                if (*left < *right) return -1;
                if (*left > *right) return 1;
                return 0;
            }
            int main() {
                PriorityQueue<int> items = new(Compare);
                for (int i = 199; i >= 0; i--) items.Enqueue(i / 2);
                for (int i = 0; i < 200; i++) if (items.Dequeue() != i / 2) return 1;
                if (!items.IsEmpty) return 2;
                for (int round = 0; round < 25; round++) {
                    for (int i = 0; i < 100; i++) items.Enqueue((i * 37) % 100);
                    for (int i = 0; i < 50; i++) if (items.Dequeue() != i) return 3;
                    for (int i = 0; i < 50; i++) items.Enqueue(i);
                    for (int i = 0; i < 100; i++) if (items.Dequeue() != i) return 4;
                }
                int output = 77;
                if (items.TryDequeue(ref output) || output != 77) return 5;
                items.Enqueue(42);
                if (items.Top != 42 || !items.TryDequeue(ref output) || output != 42) return 6;
                items.Enqueue(1); items.Clear();
                if (!items.IsEmpty) return 7;
                return 0;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void OwningElementsSurviveSiftingAndDestroyExactlyOnce()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Memory; using std::Queries; using std::Collections;
            struct Item {
                private int* destroyed;
                public int Priority;
                public String Name;
                public Item(int* counter, int priority) {
                    destroyed = counter; Priority = priority; Name = s"owned";
                }
                ~Item() { (*destroyed)++; }
            }
            int Compare(immutable(Item)* left, immutable(Item)* right) => left.Priority - right.Priority;
            int main() {
                int destroyed = 0;
                {
                    PriorityQueue<Item> items = new(Compare);
                    for (int i = 49; i >= 0; i--) items.Enqueue(new Item(&destroyed, i));
                    for (int i = 0; i < 25; i++) {
                        Item value = items.Dequeue();
                        if (value.Priority != i || value.Name.Length != 5) return 1;
                    }
                    if (destroyed != 25 || items.Count != 25) return 2;
                    PriorityQueue<Item> moved = move items;
                    moved.Clear();
                    if (destroyed != 50) return 3;
                }
                return destroyed - 50;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void FailedComparisonDestroysAnOwningInsertionArgumentOnce()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Memory; using std::Queries; using std::Collections;
            bool fail = false;
            struct Item {
                private int* destroyed;
                public int Priority;
                public Item(int* counter, int priority) { destroyed = counter; Priority = priority; }
                ~Item() { (*destroyed)++; }
            }
            int Compare(immutable(Item)* left, immutable(Item)* right) {
                if (fail) throw new Exception("comparison failed");
                return left.Priority - right.Priority;
            }
            int main() {
                int destroyed = 0;
                {
                    PriorityQueue<Item> items = new(Compare);
                    items.Enqueue(new Item(&destroyed, 4)); items.Enqueue(new Item(&destroyed, 9));
                    fail = true;
                    try { items.Enqueue(new Item(&destroyed, 1)); return 1; } catch (ref Exception error) {}
                    if (destroyed != 1 || items.Count != 2 || items.Top.Priority != 4) return 2;
                    fail = false;
                    { Item first = items.Dequeue(); if (first.Priority != 4) return 3; }
                    { Item last = items.Dequeue(); if (last.Priority != 9) return 4; }
                }
                return destroyed - 3;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ThrowingComparisonPreservesBothInsertionAndRemovalState()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Memory; using std::Queries; using std::Collections;
            bool fail = false;
            int Compare(readonly(int)* left, readonly(int)* right) {
                if (fail) throw new Exception("comparison failed");
                return *left - *right;
            }
            int main() {
                PriorityQueue<int> items = new(Compare);
                for (int i = 0; i < 10; i++) items.Enqueue(i);
                fail = true;
                try { items.Enqueue(-1); return 1; } catch (ref Exception error) {}
                try { items.Dequeue(); return 2; } catch (ref Exception error) {}
                if (items.Count != 10 || items.Top != 0) return 3;
                fail = false;
                for (int i = 0; i < 10; i++) if (items.Dequeue() != i) return 4;
                return 0;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }
}
