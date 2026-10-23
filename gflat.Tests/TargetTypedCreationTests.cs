using gflat.CompileExceptions;

namespace gflat.Tests;

public sealed class TargetTypedCreationTests
{
    private const string Thing = "struct Thing { public int value; public Thing(int n = 42) { value = n; } } ";
    private static void Succeeds(string source, bool managed = false)
    {
        var files = new List<SourceFile>();
        if (managed)
            foreach (string name in new[] { "libc/GC.gf", "libc/TracingCollector.gf" })
                files.Add(new("std/" + name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))));
        files.Add(new("main.gf", Thing + source));
        var result = CompilerTestHelper.Run(files);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Fact]
    public void ValuesRawPointersAndPrimitiveConstructionFollowTheTarget() => Succeeds("""
        int main() {
            Thing value = new();
            Thing* heap = new(17);
            int* scalar = new(23);
            int number = new(2);
            int result = value.value + heap.value + *scalar + number;
            delete heap; delete scalar;
            return result - 84;
        }
        """);

    [Fact]
    public void ManagedNullableAndCustomCollectorTargetsArePreserved() => Succeeds("""
        alias OwnGC = std::TracingCollector;
        void Check() {
            Thing^ value = new(20);
            managed<Thing, OwnGC>? other = new(22);
            GC::Collect();
            if (value.value + other!.value != 42) throw new Exception();
        }
        int main() { Check(); return (int)GC::Collect() - 2; }
        """, managed: true);

    [Fact]
    public void AssignmentsFieldsAndReturnsSupplyTargets() => Succeeds("""
        Thing Make() => new(21);
        Thing* Allocate() => new(20);
        struct Holder { public Thing value = new(1); public Thing Read() => new(2); }
        int main() {
            Holder holder = new();
            Thing local = new(3);
            local = new(4);
            holder.value = new(5);
            Thing* heap = Allocate();
            int result = Make().value + heap.value + local.value + holder.value.value + holder.Read().value;
            delete heap;
            return result - 52;
        }
        """);

    [Fact]
    public void CallsAndFunctionPointersUseTheirParameterTypes() => Succeeds("""
        int Read(Thing* value) { int n = value.value; delete value; return n; }
        int Pick(Thing value, int tag) => value.value + tag;
        int Pick(Thing* value, bool tag) => 100;
        struct Wrapper { public Thing item; public Wrapper(Thing value) { item = value; } }
        int main() {
            int(Thing*)* callback = Read;
            Wrapper wrapped = new(new(10));
            return Read(new(10)) + callback(new(10)) + Pick(new(10), 2) + wrapped.item.value - 42;
        }
        """);

    [Fact]
    public void GenericSpecializationsResolveCreationIndependently() => Succeeds("""
        T Make<T>() => new();
        int Read<T>(T witness, T value) => 42;
        int main() {
            Thing a = Make<Thing>();
            Thing* b = Make<Thing*>();
            int result = a.value + b.value + Read(a, new());
            delete b;
            return result - 126;
        }
        """);

    [Fact]
    public void ReadonlyAndNullableRawTargetsStillConstructMutableNonNullObjects() => Succeeds("""
        int main() {
            immutable(Thing)*? value = new();
            int result = value!.value;
            delete (Thing*)value!;
            return result - 42;
        }
        """);

    [Fact]
    public void PlacementConstructionUsesTheTargetWithoutAllocating() => Succeeds("""
        int main() {
            Thing storage = new(0);
            Thing* placed = new(42) at &storage;
            return placed.value - 42;
        }
        """);

    [Fact]
    public void TypedArraysCastsAndPropertiesProvideTargets() => Succeeds("""
        struct Holder { public Thing Value { get; set; } = new(0); }
        int main() {
            Thing[2] values = [new(10), new(20)];
            Holder holder = new();
            holder.Value = new(5);
            return values[0].value + values[1].value + holder.Value.value + ((Thing)new(7)).value - 42;
        }
        """);

    [Fact]
    public void BaseConstructorsAndGenericExtensionsAcceptContextualArguments() => Succeeds("""
        class Base { public int value; public Base(Thing item) { value = item.value; } }
        class Derived : Base { public Derived() : base(new(21)) {} }
        int Combine<T>(this T first, T second) => 21;
        int main() { Derived d = new(); Thing t = new(); return d.value + t.Combine(new()) - 42; }
        """);

    [Fact]
    public void OwningValuesAndFailedConstructionHaveNormalCleanup() => Succeeds("""
        static class Counts { public static int destroyed; }
        struct Owner {
            public Owner() {}
            ~Owner() { Counts::destroyed++; }
        }
        struct Broken { public Broken(Thing item) { throw new Exception(); } }
        Owner Make() => new();
        int main() {
            { Owner value = Make(); }
            { Owner* value = new(); delete value; }
            try { Broken* value = new(new()); return 1; } catch {}
            return Counts::destroyed - 2;
        }
        """);

    [Fact]
    public void ManagedClassesRemainAliveAndAreCollectedNormally() => Succeeds("""
        class Node { public int value; public Node(int n) { value = n; } }
        void Check() { Node^ node = new(42); GC::Collect(); if (node.value != 42) throw new Exception(); }
        int main() { Check(); return (int)GC::Collect() - 1; }
        """, managed: true);

    [Fact]
    public void ExplicitManagedTargetDoesNotUseTheDefaultCollector() => Succeeds("""
        alias GC = Other;
        static class Other {
            public static void* Allocate(nuint bytes, nuint alignment) { throw new Exception("Wrong collector"); }
            public static void ReferenceAdded(void* slot) nothrow {}
            public static void ReferenceRemoved(void* slot) nothrow {}
        }
        static class Selected {
            public static int allocations;
            public static void* Allocate(nuint bytes, nuint alignment) { allocations++; return Memory::Allocate(bytes)!; }
            public static void ReferenceAdded(void* slot) nothrow {}
            public static void ReferenceRemoved(void* slot) nothrow {}
        }
        int main() { managed<Thing, Selected> item = new(42); return item.value + Selected::allocations - 43; }
        """);

    [Fact]
    public void ConstantConstructionAndDefaultsReceiveTheirDeclaredType() => Succeeds("""
        struct Literal { public int value; public const Literal(int n) { value = n; } }
        const Literal folded = new(20);
        int Read(Literal value = new(22)) => value.value;
        int main() => folded.value + Read() - 42;
        """);

    [Fact]
    public void RawAllocationFailureThrowsBeforeConstruction() => Succeeds("""
        namespace Memory {
            public replace void*? Allocate(nuint bytes) => null;
            public replace void Free(void* address) nothrow {}
        }
        int main() {
            try { Thing* item = new(42); return 1; }
            catch (ref std::BadAllocException error) { return 0; }
        }
        """);

    [Theory]
    [InlineData("int main() { var a = new(); return 0; }", "no target type")]
    [InlineData("int main() { new(); return 0; }", "no target type")]
    [InlineData("void Use(Thing value) {} void Use(Thing* value) {} int main() { Use(new()); return 0; }", "ambiguous")]
    [InlineData("struct Outer { public Outer(Thing x) {} public Outer(Thing* x) {} } int main() { Outer x = new(new()); return 0; }", "ambiguous")]
    [InlineData("int main() { Thing x = new*(); return 0; }", "without '*'")]
    [InlineData("int main() { Thing x = new^(); return 0; }", "without '*'")]
    [InlineData("interface I {} int main() { I* x = new(); return 0; }", "constructible")]
    [InlineData("int main() { void* x = new(); return 0; }", "constructible")]
    [InlineData("int main() { Thing x = new() at (Thing*)1; return 0; }", "raw pointer target")]
    [InlineData("int main() { Thing x = new(1, 2); return 0; }", "constructor")]
    [InlineData("void Use<T>(T value) {} int main() { Use(new()); return 0; }", "infer type parameter")]
    public void MissingOrAmbiguousTargetsAndInvalidFormsAreRejected(string source, string message)
    {
        var error = Assert.Throws<TypeCheckException>(() => Compiler.Emit(Thing + source));
        Assert.Contains(message, error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
