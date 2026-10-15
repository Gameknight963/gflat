using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class ManagedCollectorSelectionTests
{
    private const string Providers = """
        extern void*? malloc(nuint bytes);
        extern int printf(readonly(char*) format, ...);
        static class First {
            public static void* Allocate(nuint bytes, nuint alignment) { printf("A"); return malloc(bytes)!; }
            public static void ReferenceAdded(void* slot) nothrow {}
            public static void ReferenceRemoved(void* slot) nothrow {}
        }
        static class Second {
            public static void* Allocate(nuint bytes, nuint alignment) { printf("B"); return malloc(bytes)!; }
            public static void ReferenceAdded(void* slot) nothrow {}
            public static void ReferenceRemoved(void* slot) nothrow {}
        }
        alias GC = First;
        """;

    [Fact]
    public void DefaultAndExplicitCollectorsKeepDirectAddressRepresentation()
    {
        var result = CompilerTestHelper.Run(Providers + """
            int main() {
                int^ first = new^ int(20);
                managed<int, First> same = first;
                managed<int, Second> second = new^ int(22) with Second;
                if (sizeof(managed<int, First>) != sizeof(int*)) return 1;
                if (sizeof(managed<int, Second>) != sizeof(int*)) return 2;
                return *same + *second;
            }
            """);
        Assert.Equal(42, result.ExitCode);
        Assert.Equal("AB", result.StandardOutput);
    }

    [Fact]
    public void GenericFactorySubstitutesCollectorAndPointee()
    {
        Assert.Equal(42, CompilerTestHelper.Run(Providers + """
            managed<T, C> Make<T, C>(T value) => new^ T(value) with C;
            int main() { managed<int, Second> item = Make<int, Second>(42); return *item; }
            """).ExitCode);
    }

    [Theory]
    [InlineData("managed<int, Second> second = first;")]
    [InlineData("managed<int, Second> second = (managed<int, Second>)first;")]
    [InlineData("managed<int, Second>? second = first;")]
    [InlineData("readonly(managed<int, Second>) second = first;")]
    public void CollectorsCannotBeImplicitlyMixedOrCastBetween(string operation)
    {
        Assert.Throws<TypeCheckException>(() => Compiler.Emit(Providers + "int main() { int^ first = new^ int(1); " + operation + " return 0; }"));
    }

    [Fact]
    public void ReferenceNotificationsUseTheOwningCollector()
    {
        string ir = Compiler.Emit(Providers + "int main() { managed<int, Second> second = new^ int(42) with Second; return *second; }");
        Assert.Contains("@gflat$Second$ReferenceAdded", ir);
        Assert.Contains("@gflat$Second$ReferenceRemoved", ir);
        Assert.DoesNotContain("@gflat$GC$", ir);
        Assert.DoesNotContain("__gflat_gc_", ir);
    }

    [Fact]
    public void ProviderMayBuildManagedReferenceFromOwnedRawStorage()
    {
        Assert.Equal(42, CompilerTestHelper.Run(Providers + """
            managed<int, Second> Adopt(int* value) => (managed<int, Second>)value;
            int main() { int* storage = (int*)malloc((nuint)sizeof(int))!; *storage = 42; managed<int, Second> reference = Adopt(storage); return *reference; }
            """).ExitCode);
    }

    [Fact]
    public void DefaultSelectionIsGlobalAndExplicitSelectionUsesLexicalScope()
    {
        var result = CompilerTestHelper.Run(Providers + """
            namespace Local {
                alias GC = Second;
                int Read() {
                    int^ globalValue = new^ int(20);
                    managed<int, Second> local = new^ int(22) with GC;
                    return *globalValue + *local;
                }
            }
            int main() => Local::Read();
            """);
        Assert.Equal(42, result.ExitCode);
        Assert.Equal("AB", result.StandardOutput);
    }

    [Fact]
    public void GenericInferenceAndSpecializationRetainCollectorIdentity()
    {
        Assert.Equal(42, CompilerTestHelper.Run(Providers + """
            struct Holder<T> { public T value; public Holder(T item) { value = item; } }
            int Read<C>(managed<int, C> item) => *item;
            int main() {
                Holder<managed<int, First>> first = new Holder<managed<int, First>>(new^ int(20));
                Holder<managed<int, Second>> second = new Holder<managed<int, Second>>(new^ int(22) with Second);
                return Read(first.value) + Read(second.value);
            }
            """).ExitCode);
    }

    [Fact]
    public void ManagedGraphsCannotSilentlyCrossCollectorBoundaries()
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit(Providers + """
            class Node { public managed<int, Second>? child; }
            int main() { Node^ root = new^ Node(); return 0; }
            """));
        Assert.Contains("different collector", error.Message);
    }
}
