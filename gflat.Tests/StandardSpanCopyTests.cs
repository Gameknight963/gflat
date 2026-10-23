using System.Text;
using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class StandardSpanCopyTests
{
    private static IEnumerable<SourceFile> Sources(string program)
        => new[] { "Collections/IEnumerable.gf", "Collections/Enumerable.gf", "Collections/SpanCopy.gf", "Span.gf", "Vector.gf",
            "IStringConvertable.gf", "IHashable.gf", "IEquatable.gf", "IComparable.gf", "Hashing.gf", "String.gf", "ByteSearch.gf" }
            .Select(file => new SourceFile(file, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", file))))
            .Append(new SourceFile("copy.gf", program));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EverySmallOverlapMatchesArrayCopyAndPreservesDestinationTail(int optimization)
    {
        var source = new StringBuilder("using std; extern int puts(immutable(char*) text); int main() {\n");
        for (int count = 0; count <= 8; count++)
        for (int from = 0; from <= 8 - count; from++)
        for (int to = 0; to <= 8 - count; to++)
        {
            int[] expected = Enumerable.Range(0, 8).ToArray();
            Array.Copy(expected, from, expected, to, count);
            source.AppendLine("{ int[8] buffer = [0,1,2,3,4,5,6,7]; Span<int> values = new((int*)buffer,8);");
            source.AppendLine($"values.AsImmutable().Slice({from},{count}).CopyTo(values.Slice({to}));");
            for (int i = 0; i < expected.Length; i++)
                source.AppendLine($"if (buffer[{i}] != {expected[i]}) {{ puts(\"overlap {from}/{to}/{count}/{i}\"); return 1; }}");
            source.AppendLine("}");
        }
        source.AppendLine("return 0; }");
        Run(source.ToString(), optimization);
    }

    private const string Owner = """
        using std;
        int constructed = 0; int destroyed = 0; int budget = -1;
        struct Owned {
            public int Value;
            public String Text;
            public Owned(int value) { Value = value; Text = s"data"; constructed++; }
            public copy Owned(immutable(Owned)* source) {
                if (budget == 0) throw new Exception("copy failed");
                if (budget > 0) budget--;
                Value = source.Value; Text = source.Text; constructed++;
            }
            ~Owned() { destroyed++; }
        }
        """;

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OwningElementsAreDeepCopiedAndExactSelfCopyDoesNothing(int optimization)
        => Run(Owner + """
            int main() { {
                Vector<Owned> values = new();
                for (int i = 0; i < 5; i++) values.Add(new Owned(i));
                Span<Owned> view = values.AsSpan();
                view.AsImmutable().Slice(0,4).CopyTo(view.Slice(1));
                for (nuint i = 1; i < 5; i++) if (view[i].Value != (int)i - 1) return 1;
                view[0].Text.AppendLiteral("!",1);
                if (view[0].Text.Length != 5 || view[1].Text.Length != 4) return 2;
                if (constructed != 9 || destroyed != 4) return 3;
                budget = 0;
                view.AsImmutable().CopyTo(view);
                if (constructed != 9 || destroyed != 4) return 4;
                budget = -1;
                Vector<Owned> destination = new(); for (int i = 0; i < 6; i++) destination.Add(new Owned(100));
                view.AsImmutable().CopyTo(destination.AsSpan());
                if (destination[0].Text.Length != 5 || destination[5].Value != 100) return 5;
                if (constructed - destroyed != 11) return 6;
            }
            return constructed - destroyed; }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CapacityFailurePrecedesCopyAndThrowingCopiesPreserveRemainingOwners(int optimization)
        => Run(Owner + """
            int main() { {
                Vector<Owned> values = new(); for (int i = 0; i < 5; i++) values.Add(new Owned(i));
                Span<Owned> view = values.AsSpan();
                int caught = 0; budget = 0;
                try { view.AsImmutable().CopyTo(view.Slice(1)); } catch { caught++; }
                if (caught != 1 || constructed != 5 || destroyed != 0) return 1;
                budget = 1;
                try { view.AsImmutable().Slice(0,4).CopyTo(view.Slice(1)); } catch { caught++; }
                if (caught != 2 || constructed != 6 || destroyed != 1) return 2;
                for (nuint i = 0; i < 4; i++) if (view[i].Value != (int)i) return 3;
                if (view[4].Value != 3) return 4;
                budget = 1;
                try { view.AsImmutable().Slice(1,4).CopyTo(view.Slice(0,4)); } catch { caught++; }
                if (caught != 3 || constructed != 7 || destroyed != 2) return 5;
                if (view[0].Value != 1 || view[1].Value != 1 || view[2].Value != 2 || view[4].Value != 3) return 6;
                Span<immutable(Owned)> empty = new(); empty.CopyTo(view);
                if (constructed - destroyed != 5) return 7;
            }
            return constructed - destroyed; }
            """, optimization);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CopyFailureAfterOwningFieldInitializationCleansTemporaryAndPreservesDestination(int optimization)
        => Run("""
            using std;
            int allocations = 0; int releases = 0; int copies = 0;
            int constructed = 0; int destroyed = 0; int budget = -1;
            private extern void*? malloc(nuint bytes);
            private extern void free(void* storage);
            namespace Memory {
                public replace void*? Allocate(nuint bytes) {
                    void*? result = malloc(bytes); if (result != null) allocations++; return result;
                }
                public replace void Free(void* storage) nothrow { releases++; free(storage); }
            }
            struct Item {
                public int Value; public String Text;
                public Item(int value) { Value = value; Text = s"owned"; constructed++; }
                public copy Item(immutable(Item)* source) {
                    copies++; Value = source.Value; Text = source.Text;
                    if (budget == 0) throw new Exception("copy after allocation");
                    if (budget > 0) budget--; constructed++;
                }
                ~Item() { destroyed++; }
            }
            int main() { {
                Vector<Item> values = new();
                for (int i = 0; i < 4; i++) values.Add(new Item(i));
                Span<Item> view = values.AsSpan();
                int liveAllocations = allocations - releases;
                budget = 1; bool caught = false;
                try { view.AsImmutable().Slice(0,3).CopyTo(view.Slice(1)); }
                catch { caught = true; }
                if (!caught || copies != 2 || constructed != 5 || destroyed != 1) return 1;
                if (allocations - releases != liveAllocations) return 2;
                if (view[0].Value != 0 || view[1].Value != 1 || view[2].Value != 2 || view[3].Value != 2) return 3;
                if (view[2].Text.Data == view[3].Text.Data) return 4;
                view[2].Text.AppendLiteral("!",1);
                if (view[2].Text.Length != 6 || view[3].Text.Length != 5) return 5;
                budget = 0; view.AsImmutable().CopyTo(view);
                if (copies != 2 || constructed - destroyed != 4) return 6;
            }
            if (allocations != releases || constructed != destroyed) return 7;
            return 0; }
            """, optimization);

    [Fact]
    public void CopyToCannotWriteThroughReadonlyDestination()
    {
        Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources("""
            using std;
            int main() {
                int[2] source = [1,2]; int[2] destination = [3,4];
                Span<int> input = new((int*)source,2);
                Span<int> output = new((int*)destination,2);
                input.AsImmutable().CopyTo(output.AsImmutable());
                return 0;
            }
            """)));
    }

    [Fact]
    public void NoncopyableSpansRemainUsableWithoutInstantiatingCopyTo()
        => Run("""
            using std;
            int destroyed = 0;
            struct Unique { public int Value; public Unique(int value) { Value = value; } ~Unique() { destroyed++; } }
            int main() { {
                Vector<Unique> values = new(); values.Add(new Unique(7));
                Span<Unique> view = values.AsSpan(); view[0].Value++;
                Span<immutable(Unique)> borrowed = view.AsImmutable(); if (borrowed[0].Value != 8) return 1;
            }
            return destroyed - 1; }
            """, 0);

    [Fact]
    public void CopyToDoesNotBypassNoncopyableElementOwnership()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Check(Sources("""
            using std;
            struct Unique { public int Value; public Unique(int value) { Value = value; } ~Unique() {} }
            int main() {
                Vector<Unique> source = new(); source.Add(new Unique(7));
                Vector<Unique> destination = new(); destination.Add(new Unique(8));
                source.AsImmutable().CopyTo(destination.AsSpan());
                return 0;
            }
            """)));
        Assert.Contains("copy", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void Run(string program, int optimization)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat_span_copy_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string ir = Path.Combine(directory, "copy.ll"), executable = Path.Combine(directory, "copy.exe");
            File.WriteAllText(ir, Compiler.Emit(Sources(program), options: new() { InternalizeFunctions = true }));
            var compilation = NativeToolchain.Compile(ir, executable, optimizationLevel: optimization);
            Assert.True(compilation.ExitCode == 0, compilation.StandardError);
            var result = NativeToolchain.Run(executable, []);
            Assert.True(result.ExitCode == 0, $"Native span copy exit {result.ExitCode}: {result.StandardOutput} {result.StandardError}");
        }
        finally { Directory.Delete(directory, true); }
    }
}
