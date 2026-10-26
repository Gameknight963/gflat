namespace gflat.Tests;

public sealed class StandardVectorMutationTests
{
    private static IEnumerable<SourceFile> Sources(string program) => new[] {
        "Collections/IEnumerable.gf", "Queries/Enumerable.gf", "IStringConvertable.gf", "IHashable.gf",
        "IEquatable.gf", "IComparable.gf", "Hashing/primitive_hashing.gf", "String.gf", "ByteSearch.gf", "Collections/Vector.gf", "Memory/Span.gf"
    }.Select(file => new SourceFile(file, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", file))))
        .Append(new SourceFile("main.gf", program));

    [Fact]
    public void InsertionAndRemovalPreserveOrderAtEveryPosition()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Queries; using std::Collections;
            int main() {
                Vector<int> values = new();
                values.Insert(0, 2); values.Insert(0, 1); values.Insert(2, 4); values.Insert(2, 3);
                for (nuint i = 0; i < values.Count; i++) if (values[i] != (int)i + 1) return 1;
                for (int round = 0; round < 100; round++) {
                    for (nuint index = 0; index <= values.Count; index++) {
                        values.Insert(index, 99);
                        if (values.RemoveAt(index) != 99) return 2;
                        for (nuint i = 0; i < values.Count; i++) if (values[i] != (int)i + 1) return 3;
                    }
                }
                values.Truncate(2);
                if (values.Count != 2 || values.Pop() != 2 || values.RemoveAt(0) != 1) return 4;
                values.Truncate(0);
                return 0;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void AliasedStringInsertionCopiesBeforeGrowthAndRemovalTransfers()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Queries; using std::Collections;
            int main() {
                Vector<String> values = new(); values.Reserve(4);
                values.Add(s"one"); values.Add(s"two"); values.Add(s"three"); values.Add(s"four");
                values.Insert(0, values[2]);
                if (values.Count != 5 || values[0].Length != 5 || values[3].Length != 5) return 1;
                values[0][0] = 'X';
                if (values[3].Data[0] != 't') return 2;
                String removed = values.RemoveAt(2);
                if (removed.Length != 3 || removed.Data[0] != 't' || values[2].Length != 5) return 3;
                values.Truncate(1);
                if (values.Count != 1 || values[0].Data[0] != 'X') return 4;
                return 0;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void NoncopyableElementsAreRelocatedAndDestroyedExactlyOnce()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Queries; using std::Collections;
            struct Item {
                private int* destroyed;
                public int Key;
                public Item(int* counter, int key) { destroyed = counter; Key = key; }
                ~Item() { (*destroyed)++; }
            }
            int main() {
                int destroyed = 0;
                {
                    Vector<Item> values = new();
                    for (int i = 0; i < 20; i++) values.Insert((nuint)(i / 2), new Item(&destroyed, i));
                    if (destroyed != 0 || values.Count != 20) return 1;
                    { Item removed = values.RemoveAt(5); }
                    if (destroyed != 1 || values.Count != 19) return 2;
                    values.RemoveAt(0);
                    if (destroyed != 2 || values.Count != 18) return 3;
                    values.Truncate(10);
                    if (destroyed != 10 || values.Count != 10) return 4;
                }
                return destroyed - 20;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void InvalidBoundsDestroyInsertionArgumentAndPreserveContents()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Queries; using std::Collections;
            struct Item {
                private int* destroyed;
                public int Key;
                public Item(int* counter, int key) { destroyed = counter; Key = key; }
                ~Item() { (*destroyed)++; }
            }
            int main() {
                int destroyed = 0;
                {
                    Vector<Item> values = new(); values.Add(new Item(&destroyed, 42));
                    int caught = 0;
                    try { values.Insert(2, new Item(&destroyed, 99)); } catch (ref Exception error) { caught++; }
                    try { values.RemoveAt(1); } catch (ref Exception error) { caught++; }
                    try { values.Truncate(2); } catch (ref Exception error) { caught++; }
                    if (caught != 3 || destroyed != 1 || values.Count != 1 || values[0].Key != 42) return 1;
                }
                return destroyed - 2;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void UnorderedRemovalMovesTheLastOwnerAndDestroysEveryValueOnce()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Queries; using std::Collections;
            struct Item {
                private int* destroyed;
                public int Key;
                public Item(int* counter, int key) { destroyed = counter; Key = key; }
                ~Item() { (*destroyed)++; }
            }
            int main() {
                int destroyed = 0;
                {
                    Vector<Item> values = new();
                    for (int i = 0; i < 5; i++) values.Add(new Item(&destroyed, i));
                    { Item removed = values.SwapRemoveAt(1); if (removed.Key != 1) return 1; }
                    if (destroyed != 1 || values.Count != 4 || values[1].Key != 4) return 2;
                    { Item removed = values.SwapRemoveAt(3); if (removed.Key != 3) return 3; }
                    if (destroyed != 2 || values.Count != 3 || values[0].Key != 0 || values[1].Key != 4 || values[2].Key != 2) return 4;
                    while (values.Count != 0) values.SwapRemoveAt(0);
                    try { values.SwapRemoveAt(0); return 5; } catch (ref Exception error) {}
                }
                return destroyed - 5;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ThrowingArgumentCopyHappensBeforeAnyVectorMutation()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Queries; using std::Collections;
            struct Item {
                private int* copies;
                private int* destroyed;
                private bool fail;
                public int Key;
                public Item(int* copyCount, int* destroyCount, int key, bool failCopy) {
                    copies = copyCount; destroyed = destroyCount; Key = key; fail = failCopy;
                }
                public copy Item(immutable(Item)* source) {
                    copies = (int*)source.copies; destroyed = (int*)source.destroyed;
                    Key = source.Key; fail = source.fail; (*copies)++;
                    if (fail) throw new Exception("copy failed");
                }
                ~Item() { (*destroyed)++; }
            }
            int main() {
                int copies = 0; int destroyed = 0;
                {
                    Vector<Item> values = new(); values.Add(new Item(&copies, &destroyed, 42, false));
                    Item original = new Item(&copies, &destroyed, 99, true);
                    try { values.Insert(0, original); return 1; } catch (ref Exception error) {}
                    if (copies != 1 || destroyed != 0 || values.Count != 1 || values[0].Key != 42 || original.Key != 99) return 2;
                }
                return destroyed - 2;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void FailedGrowthDestroysInsertionArgumentAndPreservesOrdering()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Queries; using std::Collections;
            extern void* malloc(nuint bytes); extern void free(void* address);
            namespace Memory {
                replace void*? Allocate(nuint bytes) { if (bytes > 64) return null; return malloc(bytes); }
                replace void Free(void* address) nothrow { free(address); }
            }
            struct Item {
                private int* destroyed;
                public int Key;
                public Item(int* counter, int key) { destroyed = counter; Key = key; }
                ~Item() { (*destroyed)++; }
            }
            int main() {
                int destroyed = 0;
                {
                    Vector<Item> values = new();
                    for (int i = 0; i < 4; i++) values.Add(new Item(&destroyed, i));
                    try { values.Insert(2, new Item(&destroyed, 99)); return 1; } catch (ref BadAllocException error) {}
                    if (destroyed != 1 || values.Count != 4) return 2;
                    for (nuint i = 0; i < 4; i++) if (values[i].Key != (int)i) return 3;
                }
                return destroyed - 5;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }
}
