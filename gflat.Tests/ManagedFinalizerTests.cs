namespace gflat.Tests;

public sealed class ManagedFinalizerTests
{
    private static IEnumerable<SourceFile> Sources(string program) => new[] { "core/IGarbageCollector.gf", "libc/TracingCollector.gf", "libc/GC.gf" }
        .Select(name => new SourceFile("Standard/" + name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", name))))
        .Append(new SourceFile("main.gf", program));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MarkedDestructorRunsForStackNativeAndManagedObjects(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            struct Resource {
                private int* count;
                public Resource(int* counter) { count = counter; }
                finalizer ~Resource() { (*count)++; }
            }
            void Allocate(int* count) { Resource^ value = new^ Resource(count); }
            int main() {
                int count = 0;
                { Resource value = new(&count); }
                if (count != 1) return 1;
                Resource* native = new* Resource(&count);
                delete native;
                if (count != 2) return 2;
                Allocate(&count);
                if (count != 2) return 3;
                if (GC::Collect() != 1 || count != 3) return 4;
                if (GC::Collect() != 0 || count != 3) return 5;
                return 0;
            }
            """), optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("struct Resource { ~Resource() {} }", "Resource")]
    [InlineData("struct Resource { ~Resource() {} } struct Outer { public Resource value = new(); }", "Outer")]
    [InlineData("class Resource { ~Resource() {} } class Outer : Resource {}", "Outer")]
    [InlineData("struct Resource { ~Resource() {} } struct Outer { public Resource[2] values = [new Resource(), new Resource()]; }", "Outer")]
    public void UnmarkedInlineAndBaseDestructorsBlockManagedAllocation(string declarations, string type)
    {
        var error = Assert.Throws<CompileExceptions.TypeCheckException>(() =>
            Compiler.Check(Sources(declarations + " int main() { " + type + "^ value = new^ " + type + "(); return 0; }")));
        Assert.Contains("finalizer", error.Message);
    }

    [Fact]
    public void MerelyDeclaringAManagedReferenceDoesNotConstructAnObject()
    {
        Compiler.Check(Sources("""
            struct Resource { ~Resource() {} }
            void Inspect(Resource^? value) {}
            int main() { Resource^? value = null; Inspect(value); return 0; }
            """));
    }

    [Fact]
    public void ACollectorMustAcceptDestructionCallbacksForFinalizableAllocations()
    {
        var error = Assert.Throws<CompileExceptions.TypeCheckException>(() => Compiler.Check("""
            alias GC = Collector;
            static class Collector {
                public static void* Allocate(nuint bytes, nuint alignment) => ((nuint)1).reinterpret<void*>();
                public static void ReferenceAdded(void* slot) nothrow {}
                public static void ReferenceRemoved(void* slot) nothrow {}
            }
            struct Resource { finalizer ~Resource() {} }
            int main() { Resource^ value = new^ Resource(); return 0; }
            """));
        Assert.Contains("RegisterDestructor", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void InlineAndBaseFinalizersAndAdoptedValuesUseOrdinaryDestruction(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            struct Part {
                private int* count;
                public Part(int* value) { count = value; }
                finalizer ~Part() { (*count)++; }
            }
            class Base {
                private int* count;
                public Base(int* value) { count = value; }
                finalizer ~Base() { (*count)++; }
            }
            class Outer : Base {
                public Part part;
                public Outer(int* count) : base(count) { part = new(count); }
            }
            void Allocate(int* count) { Outer^ value = new^ Outer(count); }
            void Adopt(int* count) {
                Part* storage = new* Part(count);
                Part^ value = Standard::TracingCollector::Adopt(storage);
            }
            int main() {
                int count = 0;
                Allocate(&count);
                Adopt(&count);
                GC::Collect();
                if (count != 3) return 1;
                return 0;
            }
            """), optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void GenericFinalizersCanReadOtherUnreachableObjectsBeforeStorageIsFreed(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            class Node<T> {
                public T value;
                public Node<T>^? next;
                private int* total;
                public Node(T item, int* count) { value = item; next = null; total = count; }
                finalizer ~Node() {
                    (*total)++;
                    if (next != null && next!.value == value) (*total)++;
                }
            }
            void Allocate(int* total) {
                Node<int>^ first = new^ Node<int>(42, total);
                Node<int>^ second = new^ Node<int>(42, total);
                first.next = second;
                second.next = first;
            }
            int main() {
                int total = 0;
                Allocate(&total);
                if (GC::Collect() != 2 || total != 4) return 1;
                return 0;
            }
            """), optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AFinalizerCanAllocateAndCollectWithoutReenteringTheSweep(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            struct Resource {
                public static int destroyed;
                finalizer ~Resource() {
                    int^ temporary = new^ int(42);
                    if (*temporary == 42) destroyed++;
                    GC::Collect();
                }
            }
            void Allocate() { Resource^ value = new^ Resource(); }
            int main() {
                Allocate();
                if (GC::Collect() != 1 || Resource::destroyed != 1) return 1;
                if (GC::Collect() != 1 || Resource::destroyed != 1) return 2;
                return 0;
            }
            """), optimization);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void FailedConstructionDoesNotRegisterAnIncompleteObjectDestructor(int optimization)
    {
        var result = CompilerTestHelper.Run(Sources("""
            struct Resource {
                public static int destroyed;
                public Resource() { throw new Exception("construction failed"); }
                finalizer ~Resource() { destroyed++; }
            }
            int main() {
                try { Resource^ value = new^ Resource(); return 1; }
                catch {}
                GC::Collect();
                if (Resource::destroyed != 0) return 2;
                return 0;
            }
            """), optimization);
        Assert.Equal(0, result.ExitCode);
    }
}
