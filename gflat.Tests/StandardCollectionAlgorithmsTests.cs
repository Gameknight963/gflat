namespace gflat.Tests;

public sealed class StandardCollectionAlgorithmsTests
{
    private static IEnumerable<SourceFile> Sources(string program) => new[] {
        "Collections/IEnumerable.gf", "Queries/Enumerable.gf", "Collections/Algorithms.gf", "IStringConvertable.gf",
        "IHashable.gf", "IEquatable.gf", "IComparable.gf", "Hashing.gf", "String.gf", "ByteSearch.gf", "Collections/Vector.gf", "Span.gf"
    }.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
        .Append(new SourceFile("main.gf", program));

    [Fact]
    public void SortAndReverseHandleAllSmallLengthsAndRepeatedKeys()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Queries; using std::Collections;
            int Compare(readonly(int)* a, readonly(int)* b) => *a - *b;
            int main() {
                for (int size = 0; size < 100; size++) {
                    Vector<int> data = new();
                    for (int i = 0; i < size; i++) data.Add((i * 37 + size * 11) % 23);
                    Sort<int>(data.AsSpan(), Compare);
                    for (nuint i = 1; i < data.Count; i++) if (data[i - 1] > data[i]) return 1;
                    Reverse<int>(data.AsSpan());
                    for (nuint i = 1; i < data.Count; i++) if (data[i - 1] < data[i]) return 2;
                }
                return 0;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void BoundsFindDuplicateRangesAndInsertionPositions()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Queries; using std::Collections;
            int Compare(readonly(int)* a, readonly(int)* b) => *a - *b;
            int main() {
                Vector<int> values = new();
                for (int i = 0; i < 10; i++) { values.Add(i * 2); values.Add(i * 2); }
                for (int key = -1; key <= 20; key++) {
                    nuint lower = 0; while (lower < values.Count && values[lower] < key) lower++;
                    nuint upper = 0; while (upper < values.Count && values[upper] <= key) upper++;
                    if (LowerBound<int>(values.AsImmutable(), &key, Compare) != lower) return 1;
                    if (UpperBound<int>(values.AsImmutable(), &key, Compare) != upper) return 2;
                }
                Span<readonly(int)> empty = new(); int key = 42;
                if (LowerBound<int>(empty, &key, Compare) != 0 || UpperBound<int>(empty, &key, Compare) != 0) return 3;
                return 0;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void SortingNoncopyableOwningValuesDoesNotDestroyOrDuplicateThem()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Queries; using std::Collections;
            struct Item {
                private int* destroyed;
                public int Key;
                public String Name;
                public Item(int* counter, int key) { destroyed = counter; Key = key; Name = s"owned"; }
                ~Item() { (*destroyed)++; }
            }
            int Compare(immutable(Item)* a, immutable(Item)* b) => a.Key - b.Key;
            int main() {
                int destroyed = 0;
                {
                    Vector<Item> values = new();
                    for (int i = 49; i >= 0; i--) values.Add(new Item(&destroyed, i));
                    Sort<Item>(values.AsSpan(), Compare);
                    if (destroyed != 0) return 1;
                    for (nuint i = 0; i < values.Count; i++) if (values[i].Key != (int)i || values[i].Name.Length != 5) return 2;
                    Reverse<Item>(values.AsSpan());
                    for (nuint i = 0; i < values.Count; i++) if (values[i].Key != 49 - (int)i) return 3;
                }
                return destroyed - 50;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void BorrowedSearchEqualityAndPartitionDoNotCopyOwningElements()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Queries; using std::Collections;
            struct Item {
                private int* destroyed;
                public int Key;
                public Item(int* counter, int key) { destroyed = counter; Key = key; }
                ~Item() { (*destroyed)++; }
            }
            int Compare(immutable(Item)* a, immutable(Item)* b) => a.Key - b.Key;
            bool Equal(immutable(Item)* a, immutable(Item)* b) => a.Key == b.Key;
            bool Even(immutable(Item)* item) => item.Key % 2 == 0;
            int main() {
                int destroyed = 0;
                {
                    Vector<Item> values = new(); Vector<Item> other = new();
                    if (!IsSorted<Item>(values.AsImmutable(), Compare)) return 1;
                    if (!SequenceEqual<Item>(values.AsImmutable(), other.AsImmutable(), Equal)) return 2;
                    if (FindIndex<Item>(values.AsImmutable(), Even) != 0) return 3;
                    for (int i = 0; i < 20; i++) { values.Add(new Item(&destroyed, i)); other.Add(new Item(&destroyed, i)); }
                    if (!IsSorted<Item>(values.AsImmutable(), Compare)) return 4;
                    if (!SequenceEqual<Item>(values.AsImmutable(), other.AsImmutable(), Equal)) return 5;
                    if (FindIndex<Item>(values.AsImmutable().Slice(1), Even) != 1) return 6;
                    nuint boundary = Partition<Item>(values.AsSpan(), Even);
                    if (boundary != 10 || destroyed != 0) return 7;
                    for (nuint i = 0; i < boundary; i++) if (values[i].Key % 2 != 0) return 8;
                    for (nuint i = boundary; i < values.Count; i++) if (values[i].Key % 2 == 0) return 9;
                    if (SequenceEqual<Item>(values.AsImmutable(), other.AsImmutable(), Equal)) return 10;
                    Reverse<Item>(other.AsSpan());
                    if (IsSorted<Item>(other.AsImmutable(), Compare)) return 11;
                    other.Clear();
                    if (SequenceEqual<Item>(values.AsImmutable(), other.AsImmutable(), Equal)) return 12;
                }
                return destroyed - 40;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ThrowingComparisonLeavesEveryElementAliveAndOwned()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Queries; using std::Collections;
            int comparisons = 0;
            struct Item {
                private int* destroyed;
                public int Key;
                public Item(int* counter, int key) { destroyed = counter; Key = key; }
                ~Item() { (*destroyed)++; }
            }
            int Compare(immutable(Item)* a, immutable(Item)* b) {
                comparisons++;
                if (comparisons == 20) throw new Exception("comparison failed");
                return a.Key - b.Key;
            }
            int main() {
                int destroyed = 0;
                {
                    Vector<Item> values = new();
                    for (int i = 49; i >= 0; i--) values.Add(new Item(&destroyed, i));
                    try { Sort<Item>(values.AsSpan(), Compare); return 1; } catch (ref Exception error) {}
                    if (destroyed != 0 || values.Count != 50) return 2;
                    int sum = 0; for (nuint i = 0; i < values.Count; i++) sum += values[i].Key;
                    if (sum != 1225) return 3;
                }
                return destroyed - 50;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }
}
