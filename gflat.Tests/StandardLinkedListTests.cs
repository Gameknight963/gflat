using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StandardLinkedListTests
{
    private static IEnumerable<SourceFile> Sources(string program, bool text = false, bool gc = false)
    {
        var files = new List<string> { "core/Collections/IEnumerable.gf", "core/LinkedList.gf" };
        if (text) files.AddRange(["core/IStringConvertable.gf", "core/IHashable.gf", "core/IEquatable.gf",
            "core/IComparable.gf", "core/Hashing.gf", "core/String.gf", "core/ByteSearch.gf"]);
        if (gc) files.AddRange(["libc/GC.gf", "libc/TracingCollector.gf"]);
        return files.Select(file => new SourceFile(file, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", file))))
            .Append(new SourceFile("main.gf", program));
    }

    [Fact]
    public void EndpointAndMiddleOperationsMaintainBothLinkDirections()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            int main() {
                LinkedList<int> values = new();
                if (!values.IsEmpty || values.First != null || values.Last != null) return 1;
                LinkedList<int>::Node* middle = values.AddFirst(2);
                LinkedList<int>::Node* first = values.InsertBefore(middle, 1);
                LinkedList<int>::Node* last = values.InsertAfter(middle, 3);
                if (values.Count != 3 || values.First != first || values.Last != last) return 2;
                if (first.Previous != null || last.Next != null || middle.Previous != first || middle.Next != last) return 3;
                int expected = 1;
                foreach (int value in values) { if (value != expected) return 4; expected++; }
                if (expected != 4) return 5;
                LinkedList<int>::Node*? cursor = values.Last; expected = 3;
                while (cursor != null) { if (cursor!.Value != expected) return 6; expected--; cursor = cursor!.Previous; }
                if (expected != 0 || values.Remove(middle) != 2) return 7;
                if (first.Next != last || last.Previous != first) return 8;
                if (values.RemoveFirst() != 1 || values.RemoveLast() != 3) return 9;
                if (!values.IsEmpty || values.First != null || values.Last != null) return 10;
                values.AddLast(42); values.Clear(); values.Clear();
                if (!values.IsEmpty) return 11;
                return 0;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void NodesAndIteratorsRemainValidAcrossInsertionAndListMoves()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            int main() {
                LinkedList<int> original = new();
                LinkedList<int>::Node* saved = original.AddLast(42);
                for (int i = 0; i < 100; i++) original.AddLast(i);
                if (saved != original.First || saved.Value != 42) return 1;
                LinkedList<int>::Enumerator iterator = original.GetEnumerator();
                LinkedList<int> moved = move original;
                original = new(); original.AddFirst(999);
                try { original.Remove(saved); return 2; } catch (ref Exception error) {}
                if (saved != moved.First || !iterator.MoveNext() || iterator.Current != 42) return 3;
                if (moved.Remove(saved) != 42 || moved.Count != 100 || original.Count != 1) return 4;
                for (int i = 0; i < 100; i++) if (moved.RemoveFirst() != i) return 5;
                return 0;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void OwningNoncopyableValuesTransferAndDestroyExactlyOnce()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            struct Unique {
                private int* destroyed;
                public int Key;
                public Unique(int* counter, int key) { destroyed = counter; Key = key; }
                ~Unique() { (*destroyed)++; }
            }
            int main() {
                int destroyed = 0;
                {
                    LinkedList<Unique> values = new();
                    LinkedList<Unique>::Node* first = values.AddLast(new Unique(&destroyed, 1));
                    values.AddFirst(new Unique(&destroyed, 0));
                    values.InsertAfter(first, new Unique(&destroyed, 2));
                    { Unique removed = values.Remove(first); if (removed.Key != 1) return 1; }
                    if (destroyed != 1 || values.Count != 2) return 2;
                    values.Clear();
                    if (destroyed != 3) return 3;
                    values.AddLast(new Unique(&destroyed, 3));
                    LinkedList<Unique> moved = move values;
                }
                return destroyed - 4;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void StringsCopyOnInsertionAndNodeValuesBorrowTheirOwnedStorage()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            int main() {
                LinkedList<String> values = new(); String original = s"source";
                LinkedList<String>::Node* node = values.AddLast(original);
                original[0] = 'X'; node.Value.Append("!", 1);
                if (node.Value.Data[0] != 's' || node.Value.Length != 7 || original.Length != 6) return 1;
                values.InsertBefore(node, s"before"); values.InsertAfter(node, s"after");
                String removed = values.Remove(node);
                if (removed.Length != 7 || values.Count != 2) return 2;
                if (values.RemoveFirst().Length != 6 || values.RemoveLast().Length != 5) return 3;
                return 0;
            }
            """, text: true));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ForeignLiveNodesAreRejectedWithoutChangingEitherList()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            int main() {
                LinkedList<int> first = new(); LinkedList<int> second = new();
                LinkedList<int>::Node* a = first.AddLast(1);
                LinkedList<int>::Node* b = second.AddLast(2);
                int caught = 0;
                try { first.Remove(b); } catch (ref Exception error) { caught++; }
                try { first.InsertBefore(b, 99); } catch (ref Exception error) { caught++; }
                try { first.InsertAfter(b, 99); } catch (ref Exception error) { caught++; }
                LinkedList<int> empty = new();
                try { empty.Remove(a); } catch (ref Exception error) { caught++; }
                if (first.Count != 1 || second.Count != 1 || a.Value != 1 || b.Value != 2 || !empty.IsEmpty) return 1;
                return caught - 4;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void FailedInsertionsCleanArgumentsAndPreserveExistingLinks()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            bool fail = false;
            extern void* malloc(nuint bytes); extern void free(void* pointer);
            namespace Memory {
                replace void*? Allocate(nuint bytes) { if (fail) return null; return malloc(bytes); }
                replace void Free(void* pointer) { free(pointer); }
            }
            struct Unique {
                private int* destroyed;
                public int Key;
                public Unique(int* counter, int key) { destroyed = counter; Key = key; }
                ~Unique() { (*destroyed)++; }
            }
            int main() {
                int destroyed = 0;
                {
                    LinkedList<Unique> values = new();
                    LinkedList<Unique>::Node* saved = values.AddLast(new Unique(&destroyed, 42));
                    fail = true; int caught = 0;
                    try { values.AddFirst(new Unique(&destroyed, 1)); } catch (ref BadAllocException error) { caught++; }
                    try { values.AddLast(new Unique(&destroyed, 2)); } catch (ref BadAllocException error) { caught++; }
                    try { values.InsertBefore(saved, new Unique(&destroyed, 3)); } catch (ref BadAllocException error) { caught++; }
                    try { values.InsertAfter(saved, new Unique(&destroyed, 4)); } catch (ref BadAllocException error) { caught++; }
                    fail = false;
                    if (caught != 4 || destroyed != 4 || values.Count != 1 || values.First != saved || values.Last != saved) return 1;
                    if (saved.Value.Key != 42 || saved.Next != null || saved.Previous != null) return 2;
                }
                return destroyed - 5;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void FailedFirstStateOrNodeAllocationLeavesAnEmptyReusableList()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            int budget = 0; int allocated = 0; int released = 0;
            extern void*? malloc(nuint bytes); extern void free(void* pointer);
            namespace Memory {
                replace void*? Allocate(nuint bytes) {
                    if (budget == 0) return null;
                    budget--; void*? result = malloc(bytes); if (result != null) allocated++; return result;
                }
                replace void Free(void* pointer) { released++; free(pointer); }
            }
            struct Unique {
                private int* destroyed;
                public Unique(int* counter) { destroyed = counter; }
                ~Unique() { (*destroyed)++; }
            }
            int main() {
                int destroyed = 0;
                {
                    LinkedList<Unique> values = new();
                    try { values.AddLast(new Unique(&destroyed)); return 1; } catch (ref BadAllocException error) {}
                    if (destroyed != 1 || !values.IsEmpty) return 2;
                    budget = 1;
                    try { values.AddFirst(new Unique(&destroyed)); return 3; } catch (ref BadAllocException error) {}
                    if (destroyed != 2 || !values.IsEmpty || values.First != null || values.Last != null) return 4;
                    budget = 1; values.AddLast(new Unique(&destroyed));
                    if (values.Count != 1) return 5;
                }
                if (destroyed != 3 || allocated != released) return 6;
                return 0;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ManagedValuesSurviveNativeNodesAndReleaseAfterRemovalAndClear()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            class Item { public Item^? Next; public int Key; public Item(int key) { Key = key; } }
            void Check() {
                LinkedList<Item^> values = new();
                for (int i = 0; i < 20; i++) {
                    Item^ value = new^ Item(i); value.Next = value;
                    values.AddLast(value); GC::Collect();
                }
                LinkedList<Item^>::Node* first = values.First!;
                LinkedList<Item^> moved = move values;
                foreach (Item^ value in moved) { GC::Collect(); if (value.Next!.Key != value.Key) throw new Exception("lost value"); }
                moved.Remove(first);
                if (GC::Collect() != 1 || moved.Count != 19) throw new Exception("removed root survived");
                moved.Clear();
                if (GC::Collect() != 19) throw new Exception("cleared roots survived");
            }
            int main() { Check(); GC::Collect(); return (int)TracingCollector::ManagedCount; }
            """, gc: true));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void EmptyOperationsAndUnpositionedEnumeratorThrow()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
            int main() {
                LinkedList<int> values = new(); int caught = 0;
                try { values.RemoveFirst(); } catch (ref Exception error) { caught++; }
                try { values.RemoveLast(); } catch (ref Exception error) { caught++; }
                LinkedList<int>::Enumerator iterator = values.GetEnumerator();
                try { int ignored = iterator.Current; } catch (ref Exception error) { caught++; }
                if (iterator.MoveNext() || iterator.MoveNext()) return 1;
                values.AddLast(42); iterator = values.GetEnumerator();
                if (!iterator.MoveNext() || iterator.Current != 42 || iterator.MoveNext()) return 2;
                try { int ignored = iterator.Current; } catch (ref Exception error) { caught++; }
                return caught - 4;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("LinkedList<int> copy = values;")]
    [InlineData("LinkedList<int>::Node copy = *values.First!;")]
    public void ListsAndIdentityBearingNodesCannotBeCopied(string statement)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources("""
            using std;
            int main() { LinkedList<int> values = new(); values.AddLast(42);
            """ + statement + " return 0; }")));
        Assert.Contains("copy", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
