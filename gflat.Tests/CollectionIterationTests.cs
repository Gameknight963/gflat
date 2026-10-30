using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class CollectionIterationTests
{
    private static IEnumerable<SourceFile> Sources(string program, bool gc = false) =>
        new[] { "core/Collections/IEnumerable.gf", "core/Queries/Enumerable.gf", "core/Memory/Span.gf", "core/Collections/Vector.gf", "core/IStringConvertable.gf", "core/IHashable.gf", "core/IEquatable.gf", "core/IComparable.gf", "core/Hashing/primitive_hashing.gf", "core/Text/String.gf", "core/Text/ByteSearch.gf" }.Concat(gc ? ["libc/GC.gf", "libc/TracingCollector.gf"] : [])
            .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
            .Append(new SourceFile("main.gf", program));

    private static void Run(string source, bool gc = false, string? output = null)
    {
        var result = CompilerTestHelper.Run(Sources(source, gc));
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
        if (output != null) Assert.Equal(output, result.StandardOutput);
    }

    [Fact]
    public void SpanVectorAndReadOnlyViewsIterateWithoutAllocating()
        => Run("""
            using Standard; using Standard::Text; using Standard::Memory; using Standard::Queries; using Standard::Collections;
            extern void*? malloc(nuint size);
            extern void free(void* storage);
            extern int printf(immutable(char*) text, ...);
            namespace Memory {
                public replace void*? Allocate(nuint size) { printf("A"); return malloc(size); }
                public replace void Free(void* pointer) nothrow { free(pointer); }
            }
            int Sum(immutable(Vector<int>)* values) {
                int sum = 0; foreach (int item in values.AsImmutable()) sum += item; return sum;
            }
            int main() {
                int[3] buffer = [1, 2, 3];
                Span<int> span = new Span<int>(buffer, 3);
                int sum = 0; foreach (int item in span) sum += item;
                Vector<int> values = new Vector<int>(); values.Add(4); values.Add(5);
                foreach (int item in values) sum += item;
                if (sum != 15 || Sum(&values) != 9) return 1;
                values.AsSpan()[(nuint)0] = 8;
                if (Sum(&values) != 13) return 2;
                foreach (int item in new Span<int>()) return 3;
                return 0;
            }
            """, output: "A");

    [Fact]
    public void IteratorChecksCurrentAndCanBorrowWritableElements()
        => Run("""
            using Standard; using Standard::Text; using Standard::Memory; using Standard::Queries; using Standard::Collections;
            int main() {
                int[1] buffer = [7]; Span<int> span = new Span<int>(buffer, 1);
                Span<int>::Enumerator iterator = span.GetEnumerator();
                try { int value = iterator.Current; return 1; } catch {}
                if (!iterator.MoveNext()) return 2;
                iterator.Current = 42;
                if (buffer[0] != 42 || iterator.MoveNext()) return 3;
                try { int value = iterator.Current; return 4; } catch {}
                Span<int>::Enumerator empty = new Span<int>::Enumerator(null, 0);
                if (empty.MoveNext() || empty.MoveNext()) return 5;
                try { int value = empty.Current; return 6; } catch {}
                try { Span<int>::Enumerator invalid = new Span<int>::Enumerator(null, 1); return 7; } catch {}
                return 0;
            }
            """);

    [Fact]
    public void OwningStringsAreCopiedFromElements()
        => Run("""
            using Standard; using Standard::Text; using Standard::Memory; using Standard::Queries; using Standard::Collections;
            int main() {
                Vector<String> values = new Vector<String>(); values.Add(s"abc");
                foreach (String item in values) { if (item.Data == values[(nuint)0].Data) return 3; item[(nuint)0] = 'z'; }
                if (values[(nuint)0].Data[0] != 'a') return 1;
                foreach (immutable(String) item in values.AsImmutable()) { if (item.Data[0] != 'a' || item.Data == values[(nuint)0].Data) return 2; }
                return 0;
            }
            """);

    [Fact]
    public void MutableManagedElementsRemainMutableAndRootedDuringCollection()
        => Run("""
            using Standard; using Standard::Text; using Standard::Memory; using Standard::Queries; using Standard::Collections;
            class Node { public int value; public Node() { value = 1; } }
            int main() {
                Vector<Node^> values = new Vector<Node^>(); values.Add(new^ Node());
                foreach (Node^ item in values) { GC::Collect(); item.value = 42; }
                foreach (immutable(Node^) item in values.AsImmutable()) { GC::Collect(); if (item.value != 42) return 1; }
                return 0;
            }
            """, gc: true);

    [Fact]
    public void DirectTemporaryVectorOwnsItsElementsUntilIterationFinishes()
        => Run("""
            using Standard; using Standard::Text; using Standard::Memory; using Standard::Queries; using Standard::Collections;
            extern void*? malloc(nuint size);
            extern void free(void* storage);
            extern int printf(immutable(char*) text, ...);
            namespace Memory {
                public replace void*? Allocate(nuint size) => malloc(size);
                public replace void Free(void* pointer) nothrow { printf("F"); free(pointer); }
            }
            Vector<String> MakeWords() {
                Vector<String> words = new Vector<String>(); words.Add(s"abc"); return words;
            }
            int main() {
                foreach (String word in MakeWords()) { if (word.Data[0] != 'a') return 1; break; }
                return 0;
            }
            """, output: "FFF");

    [Fact]
    public void NoncopyableElementsAreNotSilentlyConsumed()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit(Sources("""
            using Standard; using Standard::Text; using Standard::Memory; using Standard::Queries; using Standard::Collections;
            struct Resource { ~Resource() {} }
            int main() { Vector<Resource> values = new Vector<Resource>(); foreach (Resource item in values) {} return 0; }
            """)));
        Assert.Contains("copy", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadOnlyViewDoesNotExposeMutableManagedReferences()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit(Sources("""
            using Standard; using Standard::Text; using Standard::Memory; using Standard::Queries; using Standard::Collections;
            class Node { public int value; }
            int main() {
                Vector<Node^> values = new Vector<Node^>();
                foreach (immutable(Node^) item in values.AsImmutable()) item.value = 42;
                return 0;
            }
            """, gc: true)));
        Assert.Contains("readonly", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
