namespace gflat.Tests;

public sealed class StandardBinarySearchTests
{
    private static IEnumerable<SourceFile> Sources(string program) => new[] {
        "Collections/IEnumerable.gf", "Queries/Enumerable.gf", "Collections/span_algorithms.gf", "Memory/Span.gf", "Collections/Vector.gf"
    }.Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard/core", name))))
        .Append(new SourceFile("main.gf", program));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SortedSearchMatchesLinearFirstMatchInEitherOrdering(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard; using Standard::Memory; using Standard::Queries; using Standard::Collections;
            int calls = 0;
            int direction = 1;
            int Compare(readonly(int)* left, readonly(int)* right) {
                calls++;
                if (*left < *right) return -direction;
                if (*left > *right) return direction;
                return 0;
            }
            int main() {
                Span<readonly(int)> empty = new(); int key = 42;
                if (BinarySearch<int>(empty, &key, Compare) != 0 || calls != 0) return 1;
                for (int order = 0; order < 2; order++) {
                    if (order == 1) direction = -1;
                    for (int size = 0; size <= 128; size++) {
                        Vector<int> values = new();
                        for (int i = 0; i < size; i++) {
                            int value = i / 3 * 2;
                            if (order == 1) value = -value;
                            values.Add(value);
                        }
                        for (int candidate = -90; candidate <= 90; candidate++) {
                            key = candidate;
                            nuint expected = values.Count;
                            for (nuint i = 0; i < values.Count; i++)
                                if (values[i] == key) { expected = i; break; }
                            calls = 0;
                            if (BinarySearch<int>(values.AsImmutable(), &key, Compare) != expected) return 2;
                            if (calls > 9) return 3;
                        }
                    }
                }
                int[4] extremes = [-2147483648, -1, 0, 2147483647];
                direction = 1;
                Span<readonly(int)> view = new(extremes, 4);
                for (nuint i = 0; i < 4; i++)
                    if (BinarySearch<int>(view, &extremes[i], Compare) != i) return 4;
                return 0;
            }
            """), optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BorrowedOwnersSurviveEveryThrowingComparisonAndAliasedKey(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            using Standard; using Standard::Memory; using Standard::Queries; using Standard::Collections;
            int calls = 0; int failAt = 0;
            struct Item {
                private int* destroyed;
                public int Key;
                public Item(int* counter, int key) { destroyed = counter; Key = key; }
                ~Item() { (*destroyed)++; }
            }
            int Compare(immutable(Item)* left, immutable(Item)* right) {
                calls++;
                if (calls == failAt) throw new Exception("comparison failed");
                if (left.Key < right.Key) return -1;
                if (left.Key > right.Key) return 1;
                return 0;
            }
            int main() {
                int destroyed = 0;
                {
                    Vector<Item> values = new();
                    for (int i = 0; i < 65; i++) values.Add(new Item(&destroyed, i / 3));
                    immutable(Item)* key = &values[35];
                    if (BinarySearch<Item>(values.AsImmutable(), key, Compare) != 33) return 1;
                    int totalCalls = calls;
                    for (int failure = 1; failure <= totalCalls; failure++) {
                        failAt = failure;
                        calls = 0; bool caught = false;
                        try { BinarySearch<Item>(values.AsImmutable(), key, Compare); }
                        catch (Exception* error) { caught = true; }
                        if (!caught || destroyed != 0 || values.Count != 65) return 2;
                        for (nuint i = 0; i < values.Count; i++)
                            if (values[i].Key != (int)i / 3) return 3;
                    }
                }
                if (destroyed != 65) return 4;
                return 0;
            }
            """), optimization);
        Assert.Equal(0, result.ExitCode);
    }
}
