using gflat.CompileExceptions;

namespace gflat.Tests;

public class StandardSpanTests
{
    private static SourceFile[] Sources(string program) => new[] { "Span.gf", "ReadOnlySpan.gf" }
        .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", name))))
        .Append(new SourceFile("main.gf", program)).ToArray();
    private static void Run(string program) => Assert.Equal(0, CompilerTestHelper.Run(Sources(program)).ExitCode);

    [Fact]
    public void ViewsSlicesAndCopiesShareStorage()
        => Run("""
            using std;
            int main() {
                int[4] values = [10, 20, 30, 40];
                Span<int> span = new Span<int>(values, 4);
                Span<int> slice = span.Slice(1, 2);
                Span<int> copy = slice;
                copy[(nuint)0] += 2;
                if (values[1] != 22 || span.Length != (nuint)4 || slice.Length != (nuint)2) return 1;
                Span<int> tail = span.Slice(3);
                if (tail[(nuint)0] != 40) return 2;
                ReadOnlySpan<int> view = span.AsReadOnly();
                ReadOnlySpan<int> middle = view.Slice(1, 2);
                span[(nuint)1] = 42;
                if (middle[(nuint)0] != 42 || middle.Slice(1)[(nuint)0] != 30) return 3;
                readonly(Span<int>) borrowed = span;
                ReadOnlySpan<int> readonlyView = borrowed.AsReadOnly();
                if (readonlyView[(nuint)1] != 42) return 4;
                return 0;
            }
            """);

    [Fact]
    public void EmptyViewsAndBoundsChecksHandleMaximumIndices()
        => Run("""
            using std;
            int main() {
                Span<int> empty = new Span<int>();
                ReadOnlySpan<int> readonlyEmpty = empty.AsReadOnly();
                if (!empty.IsEmpty || empty.Data != null || !readonlyEmpty.IsEmpty) return 1;
                Span<int> slice = empty.Slice(0, 0);
                ReadOnlySpan<int> roSlice = readonlyEmpty.Slice(0);
                if (!slice.IsEmpty || !roSlice.IsEmpty) return 2;
                int[2] values = [1, 2];
                Span<int> span = new Span<int>(values, 2);
                ReadOnlySpan<int> view = span.AsReadOnly();
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
            using std;
            int main() {
                readonly(ReadOnlySpan<char>) text = new ReadOnlySpan<char>(c"a\0b", 3);
                if (text.Length != (nuint)3 || text[(nuint)2] != 'b') return 1;
                ReadOnlySpan<char> tail = text.Slice(1);
                if (tail.Length != (nuint)2 || tail[(nuint)0] != '\0') return 2;
                return 0;
            }
            """);

    [Fact]
    public void PointerElementsRespectDeepReadonlyAndStructElementsCopyByValue()
        => Run("""
            using std;
            struct Pair { public int x; }
            int main() {
                int value = 4;
                int*[1] pointers = [&value];
                Span<int*> span = new Span<int*>(pointers, 1);
                int* pointer = span[(nuint)0];
                *pointer = 7;
                ReadOnlySpan<int*> view = span.AsReadOnly();
                readonly(int)* borrowed = view[(nuint)0];
                if (*borrowed != 7) return 1;
                Pair[1] pairs = [new Pair()];
                Span<Pair> structs = new Span<Pair>(pairs, 1);
                Pair copy = structs[(nuint)0];
                copy.x = 42;
                if (pairs[0].x != 0) return 2;
                structs[(nuint)0] = copy;
                ReadOnlySpan<Pair> readonlyStructs = structs.AsReadOnly();
                Pair other = readonlyStructs[(nuint)0];
                if (other.x != 42) return 3;
                return 0;
            }
            """);

    [Fact]
    public void ReadonlyPointerElementsCannotBecomeMutable()
        => Assert.Throws<TypeCheckException>(() => Compiler.Emit(Sources("""
            using std;
            int main() {
                int n = 0; int*[1] pointers = [&n];
                ReadOnlySpan<int*> view = new ReadOnlySpan<int*>(pointers, 1);
                int* pointer = view[(nuint)0];
                return 0;
            }
            """)));

    [Fact]
    public void BorrowingAStringDoesNotAllocateOrFreeItsStorage()
    {
        var sources = Sources("""
            using std;
            extern int printf(readonly(char)* text, ...);
            namespace Allocator {
                public replace void*? Allocate(nuint size) { printf(c"A"); return malloc(size); }
                public replace void Free(void* p) { printf(c"F"); free(p); }
            }
            int main() {
                {
                    String text = s"a\0b";
                    {
                        ReadOnlySpan<char> view = new ReadOnlySpan<char>(text.Data, text.Length);
                        ReadOnlySpan<char> copy = view;
                        ReadOnlySpan<char> slice = copy.Slice(1);
                        if (slice.Length != (nuint)2 || slice[(nuint)1] != 'b') return 1;
                    }
                    if (text.Data[2] != 'b') return 2;
                    printf(c"S");
                }
                return 0;
            }
            """).Append(new SourceFile("String.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core/String.gf"))));
        var result = CompilerTestHelper.Run(sources);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("ASF", result.StandardOutput);
    }

    [Fact]
    public void IndexingDoesNotImplicitlyCopyOwningElements()
        => Assert.Throws<TypeCheckException>(() => Compiler.Emit(Sources("""
            using std;
            struct Owner { public int value; ~Owner() {} }
            int main() {
                Owner item = new Owner();
                Span<Owner> span = new Span<Owner>(&item, 1);
                Owner copy = span[(nuint)0];
                return 0;
            }
            """)));

    [Theory]
    [InlineData("readonly(Span<int>) s = new Span<int>(a, 1); s[(nuint)0] = 2;")]
    [InlineData("ReadOnlySpan<int> s = new ReadOnlySpan<int>(a, 1); s[(nuint)0] = 2;")]
    [InlineData("ReadOnlySpan<int> s = new ReadOnlySpan<int>(a, 1); int* p = s.Data;")]
    [InlineData("readonly(Span<int>) s = new Span<int>(a, 1); Span<int> mutable = s.Slice(0);")]
    public void ReadonlyViewsCannotBeUsedToModifyStorage(string body)
        => Assert.Throws<TypeCheckException>(() => Compiler.Emit(Sources("using std; int main() { int[1] a = [1]; " + body + " return 0; }")));
}
