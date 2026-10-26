namespace gflat.Tests;

public sealed class StandardVectorRangeTests
{
    private static IEnumerable<SourceFile> Sources(string program) => new[] {
        "Collections/IEnumerable.gf", "Queries/Enumerable.gf", "Collections/vector_add_range.gf",
        "IStringConvertable.gf", "IHashable.gf", "IEquatable.gf", "IComparable.gf", "Hashing/primitive_hashing.gf",
        "Text/String.gf", "Text/ByteSearch.gf", "Collections/Vector.gf", "Memory/Span.gf"
    }.Select(file => new SourceFile(file, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", file))))
        .Append(new SourceFile("main.gf", program));

    [Fact]
    public void SelfBorrowedRangesRebindAfterGrowthAndAppendIndependentStrings()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Memory; using std::Queries; using std::Collections;
            int main() {
                Vector<String> values = new(); values.Reserve(4);
                values.Add(s"one"); values.Add(s"two"); values.Add(s"three"); values.Add(s"four");
                values.AddRange(values.AsImmutable());
                if (values.Count != 8 || values[4].Length != 3 || values[6].Length != 5) return 1;
                values[0][0] = 'X';
                if (values[4].Data[0] != 'o') return 2;
                values.AddRange(values.AsImmutable().Slice(2, 3));
                if (values.Count != 11 || values[8].Length != 5 || values[9].Length != 4 || values[10].Data[0] != 'o') return 3;
                Span<immutable(String)> empty = new();
                values.AddRange(empty);
                if (values.Count != 11) return 4;
                return 0;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void SeparateAndReservedRangesAppendInOrder()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Memory; using std::Queries; using std::Collections;
            int main() {
                Vector<int> source = new(); Vector<int> target = new();
                for (int i = 0; i < 10; i++) source.Add(i);
                target.Reserve(40); target.AddRange(source.AsImmutable());
                target.AddRange(target.AsImmutable().Slice(2, 5));
                if (target.Count != 15 || target.Capacity != 40) return 1;
                for (nuint i = 0; i < 10; i++) if (target[i] != (int)i) return 2;
                for (nuint i = 10; i < 15; i++) if (target[i] != (int)i - 8) return 3;
                source[0] = 99;
                if (target[0] != 0) return 4;
                return 0;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void MerelyIncludingCopyExtensionsDoesNotRequireCopyableVectorElements()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Memory; using std::Queries; using std::Collections;
            struct Unique {
                private int* destroyed;
                public Unique(int* counter) { destroyed = counter; }
                ~Unique() { (*destroyed)++; }
            }
            int main() {
                int destroyed = 0;
                { Vector<Unique> values = new(); values.Add(new Unique(&destroyed)); values.Insert(0, new Unique(&destroyed)); }
                return destroyed - 2;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void FailedReservationAndCountOverflowPreserveExistingElements()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Memory; using std::Queries; using std::Collections;
            extern void* malloc(nuint bytes); extern void free(void* address);
            namespace Memory {
                replace void*? Allocate(nuint bytes) { if (bytes > 16) return null; return malloc(bytes); }
                replace void Free(void* address) nothrow { free(address); }
            }
            int main() {
                Vector<int> values = new();
                for (int i = 0; i < 4; i++) values.Add(i);
                int* original = &values[0];
                try { values.AddRange(values.AsImmutable()); return 1; } catch (ref BadAllocException error) {}
                if (values.Count != 4 || &values[0] != original) return 2;
                for (nuint i = 0; i < 4; i++) if (values[i] != (int)i) return 3;
                Span<readonly(int)> excessive = new(original, (nuint)0 - (nuint)1);
                try { values.AddRange(excessive); return 4; } catch (ref Exception error) {}
                if (values.Count != 4 || &values[0] != original) return 5;
                return 0;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void CopyFailureRetainsSuccessfulPrefixAndDestroysEveryConstructedValueOnce()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Memory; using std::Queries; using std::Collections;
            struct Item {
                private int* copies;
                private int* destroyed;
                public int Key;
                public Item(int* copyCount, int* destroyCount, int key) { copies = copyCount; destroyed = destroyCount; Key = key; }
                public copy Item(immutable(Item)* source) {
                    copies = (int*)source.copies; destroyed = (int*)source.destroyed; Key = source.Key;
                    (*copies)++;
                    if (Key == 3) throw new Exception("copy failed");
                }
                ~Item() { (*destroyed)++; }
            }
            int main() {
                int destroyed = 0; int copies = 0;
                {
                    Vector<Item> source = new(); Vector<Item> target = new();
                    for (int i = 1; i <= 4; i++) source.Add(new Item(&copies, &destroyed, i));
                    target.Add(new Item(&copies, &destroyed, 99));
                    try { target.AddRange(source.AsImmutable()); return 1; } catch (ref Exception error) {}
                    if (copies != 3 || destroyed != 0 || target.Count != 3 || source.Count != 4) return 2;
                    if (target[0].Key != 99 || target[1].Key != 1 || target[2].Key != 2) return 3;
                }
                return destroyed - 7;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }
}
