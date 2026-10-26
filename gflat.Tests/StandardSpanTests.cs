using gflat.CompileExceptions;

namespace gflat.Tests;

public class StandardSpanTests
{
    private static SourceFile[] Sources(string program) => new[] { "Collections/IEnumerable.gf", "Queries/Enumerable.gf", "Memory/Span.gf", "IHashable.gf", "IEquatable.gf", "IComparable.gf", "Hashing/primitive_hashing.gf" }
        .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
        .Append(new SourceFile("main.gf", program)).ToArray();
    private static void Run(string program) => Assert.Equal(0, CompilerTestHelper.Run(Sources(program)).ExitCode);

    [Fact]
    public void ViewsSlicesAndCopiesShareStorage()
        => Run("""
            using std; using std::Queries; using std::Collections;
            int main() {
                int[4] values = [10, 20, 30, 40];
                Span<int> span = new Span<int>(values, 4);
                Span<int> slice = span.Slice(1, 2);
                Span<int> copy = slice;
                copy[(nuint)0] += 2;
                if (values[1] != 22 || span.Length != (nuint)4 || slice.Length != (nuint)2) return 1;
                Span<int> tail = span.Slice(3);
                if (tail[(nuint)0] != 40) return 2;
                Span<readonly(int)> view = span.AsImmutable();
                Span<readonly(int)> middle = view.Slice(1, 2);
                span[(nuint)1] = 42;
                if (middle[(nuint)0] != 42 || middle.Slice(1)[(nuint)0] != 30) return 3;
                immutable(Span<int>) borrowed = span;
                Span<readonly(int)> readonlyView = borrowed.AsImmutable();
                if (readonlyView[(nuint)1] != 42) return 4;
                return 0;
            }
            """);

    [Fact]
    public void EmptyViewsAndBoundsChecksHandleMaximumIndices()
        => Run("""
            using std; using std::Queries; using std::Collections;
            int main() {
                Span<int> empty = new Span<int>();
                Span<readonly(int)> readonlyEmpty = empty.AsImmutable();
                if (!empty.IsEmpty || empty.Data != null || !readonlyEmpty.IsEmpty) return 1;
                Span<int> slice = empty.Slice(0, 0);
                Span<readonly(int)> roSlice = readonlyEmpty.Slice(0);
                if (!slice.IsEmpty || !roSlice.IsEmpty) return 2;
                int[2] values = [1, 2];
                Span<int> span = new Span<int>(values, 2);
                Span<readonly(int)> view = span.AsImmutable();
                nuint max = (nuint)0 - (nuint)1;
                int caught = 0;
                try { empty[(nuint)0]; } catch { caught++; }
                try { readonlyEmpty[(nuint)0]; } catch { caught++; }
                try { span[(nuint)2] = 3; } catch { caught++; }
                try { view[max]; } catch { caught++; }
                try { span.Slice(max, 1); } catch { caught++; }
                try { view.Slice(1, max); } catch { caught++; }
                try { span.Slice(3); } catch { caught++; }
                if (caught != 7 || values[0] != 1 || values[1] != 2) return 3;
                Span<int> end = span.Slice(2);
                if (!end.IsEmpty) return 4;
                return 0;
            }
            """);

    [Fact]
    public void ReadOnlyViewAcceptsLiteralStorageAndPreservesEmbeddedNulls()
        => Run("""
            using std; using std::Queries; using std::Collections;
            int main() {
                immutable(Span<readonly(char)>) text = new Span<readonly(char)>("a\0b", 3);
                if (text.Length != (nuint)3 || text[(nuint)2] != 'b') return 1;
                Span<readonly(char)> tail = text.AsImmutable().Slice(1);
                if (tail.Length != (nuint)2 || tail[(nuint)0] != '\0') return 2;
                return 0;
            }
            """);

    [Fact]
    public void PointerElementsRespectDeepReadonlyAndStructElementsCopyByValue()
        => Run("""
            using std; using std::Queries; using std::Collections;
            struct Pair { public int x; }
            int main() {
                int value = 4;
                int*[1] pointers = [&value];
                Span<int*> span = new Span<int*>(pointers, 1);
                int* pointer = span[(nuint)0];
                *pointer = 7;
                Span<immutable(int*)> view = span.AsImmutable();
                readonly(int)* borrowed = view[(nuint)0];
                if (*borrowed != 7) return 1;
                Pair[1] pairs = [new Pair()];
                Span<Pair> structs = new Span<Pair>(pairs, 1);
                Pair copy = structs[(nuint)0];
                copy.x = 42;
                if (pairs[0].x != 0) return 2;
                structs[(nuint)0] = copy;
                Span<immutable(Pair)> readonlyStructs = structs.AsImmutable();
                Pair other = readonlyStructs[(nuint)0];
                if (other.x != 42) return 3;
                return 0;
            }
            """);

    [Fact]
    public void ReadonlyPointerElementsCannotBecomeMutable()
        => Assert.Throws<TypeCheckException>(() => Compiler.Emit(Sources("""
            using std; using std::Queries; using std::Collections;
            int main() {
                int n = 0; int*[1] pointers = [&n];
                Span<immutable(int*)> view = new Span<immutable(int*)>(pointers, 1);
                int* pointer = view[(nuint)0];
                return 0;
            }
            """)));

    [Fact]
    public void BorrowingAStringDoesNotAllocateOrFreeItsStorage()
    {
        var sources = Sources("""
            using std; using std::Queries; using std::Collections;
            extern void*? malloc(nuint size);
            extern void free(void* storage);
            extern int printf(readonly(char)* text, ...);
            namespace Memory {
                public replace void*? Allocate(nuint size) { printf("A"); return malloc(size); }
                public replace void Free(void* p) nothrow { printf("F"); free(p); }
            }
            int main() {
                {
                    String text = s"a\0b";
                    {
                        Span<readonly(char)> view = new Span<readonly(char)>(text.Data, text.Length);
                        Span<readonly(char)> copy = view;
                        Span<readonly(char)> slice = copy.Slice(1);
                        if (slice.Length != (nuint)2 || slice[(nuint)1] != 'b') return 1;
                    }
                    if (text.Data[2] != 'b') return 2;
                    printf("S");
                }
                return 0;
            }
            """).Append(new SourceFile("String.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/String.gf"))))
                .Append(new SourceFile("ByteSearch.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/ByteSearch.gf"))))
                .Append(new SourceFile("IStringConvertable.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/IStringConvertable.gf"))));
        var result = CompilerTestHelper.Run(sources);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("ASF", result.StandardOutput);
    }

    [Fact]
    public void IndexingDoesNotImplicitlyCopyOwningElements()
        => Assert.Throws<TypeCheckException>(() => Compiler.Emit(Sources("""
            using std; using std::Queries; using std::Collections;
            struct Owner { public int value; ~Owner() {} }
            int main() {
                Owner item = new Owner();
                Span<Owner> span = new Span<Owner>(&item, 1);
                Owner copy = span[(nuint)0];
                return 0;
            }
            """)));

    [Theory]
    [InlineData("immutable(Span<int>) s = new Span<int>(a, 1); s[(nuint)0] = 2;")]
    [InlineData("Span<readonly(int)> s = new Span<readonly(int)>(a, 1); s[(nuint)0] = 2;")]
    [InlineData("Span<readonly(int)> s = new Span<readonly(int)>(a, 1); int* p = s.Data;")]
    [InlineData("immutable(Span<int>) s = new Span<int>(a, 1); Span<int> mutable = s.Slice(0);")]
    public void ReadonlyViewsCannotBeUsedToModifyStorage(string body)
        => Assert.Throws<TypeCheckException>(() => Compiler.Emit(Sources("using std; using std::Queries; using std::Collections; int main() { int[1] a = [1]; " + body + " return 0; }")));
}
