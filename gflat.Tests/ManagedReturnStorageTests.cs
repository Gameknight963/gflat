namespace gflat.Tests;

public sealed class ManagedReturnStorageTests
{
    // This collector deliberately reclaims at the last ReferenceRemoved call.
    // Poisoning retired storage makes a gap between callee and caller roots
    // observable without relying on a system allocator's reuse behavior.
    private const string Runtime = """
        extern void* rc_allocate(nuint bytes, nuint alignment);
        extern void rc_added(void* slot);
        extern void rc_removed(void* slot);
        extern int rc_live_count();
        extern int rc_first_references();
        alias GC = TestCollector;
        static class TestCollector {
            public static void* Allocate(nuint bytes, nuint alignment) => rc_allocate(bytes, alignment);
            public static void ReferenceAdded(void* slot) nothrow => rc_added(slot);
            public static void ReferenceRemoved(void* slot) nothrow => rc_removed(slot);
        }
        """;

    private const string NativeRuntimeSource = """
        #include <cstdlib>
        #include <cstring>
        #include <vector>
        struct Allocation { void* address; std::size_t size; int references; bool alive; };
        static std::vector<Allocation> allocations;
        static Allocation* find(void* address) {
            for (auto& allocation : allocations)
                if (allocation.address == address) return &allocation;
            return nullptr;
        }
        extern "C" void* rc_allocate(std::size_t bytes, std::size_t alignment) {
            void* address = std::calloc(1, bytes ? bytes : 1);
            if (!address || reinterpret_cast<std::size_t>(address) % alignment) std::abort();
            allocations.push_back({address, bytes, 0, true});
            return address;
        }
        extern "C" void rc_added(void* slot) noexcept {
            void* target;
            std::memcpy(&target, slot, sizeof(target));
            if (!target) return;
            auto* allocation = find(target);
            if (!allocation || !allocation->alive) std::abort();
            ++allocation->references;
        }
        extern "C" void rc_removed(void* slot) noexcept {
            void* target;
            std::memcpy(&target, slot, sizeof(target));
            if (!target) return;
            auto* allocation = find(target);
            if (!allocation || !allocation->alive || allocation->references <= 0) std::abort();
            if (--allocation->references == 0) {
                allocation->alive = false;
                std::memset(allocation->address, 0xA5, allocation->size);
            }
        }
        extern "C" int rc_live_count() {
            int count = 0;
            for (auto& allocation : allocations) if (allocation.alive) ++count;
            return count;
        }
        extern "C" int rc_first_references() {
            return allocations.empty() ? 0 : allocations.front().references;
        }
        """;

    private static void Succeeds(string source, bool vector = false)
    {
        string directory = Path.Combine(Path.GetTempPath(), "gflat-managed-return-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string ir = Path.Combine(directory, "program.ll");
            string runtime = Path.Combine(directory, "runtime.cpp");
            string executable = Path.Combine(directory, OperatingSystem.IsWindows() ? "program.exe" : "program");
            string emitted = vector
                ? Compiler.Emit(new[] { "core/Vector.gf", "core/Span.gf" }
                    .Select(name => new SourceFile(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
                    .Append(new SourceFile("main.gf", Runtime + source)))
                : Compiler.Emit(Runtime + source);
            File.WriteAllText(ir, emitted);
            File.WriteAllText(runtime, NativeRuntimeSource);
            foreach (int optimization in new[] { 0, 2 })
            {
                var compilation = NativeToolchain.CompileCpp(runtime, executable, [ir], optimizationLevel: optimization);
                Assert.True(compilation.ExitCode == 0, $"O{optimization}: {compilation.StandardError}\nIR:\n{File.ReadAllText(ir)}");
                ProcessResult result;
                try { result = NativeToolchain.Run(executable, []); }
                catch (TimeoutException error)
                {
                    throw new Exception($"O{optimization}: {error.Message}\nIR:\n{File.ReadAllText(ir)}", error);
                }
                Assert.True(result.ExitCode == 0, $"O{optimization}, exit {result.ExitCode}: {result.StandardError}\nIR:\n{File.ReadAllText(ir)}");
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void DirectReturnRemainsOwnedAcrossCalleeCleanup() => Succeeds("""
        int^ Create() { int^ value = new^ int(42); return value; }
        int main() {
            { int^ value = Create(); if (*value != 42) return 1; }
            return rc_live_count();
        }
        """);

    [Fact]
    public void AggregateReturnProtectsItsManagedFields() => Succeeds("""
        struct Box { public int^ item; public Box(int^ value) { item = value; } }
        Box Create() { Box box = new Box(new^ int(42)); return box; }
        int main() {
            { Box box = Create(); if (*box.item != 42) return 101; }
            return rc_live_count();
        }
        """);

    [Fact]
    public void FixedBufferReturnProtectsEveryElement() => Succeeds("""
        int^[2] Create() { int^[2] values = [new^ int(20), new^ int(22)]; return values; }
        int main() {
            { int^[2] values = Create(); if (*values[0] + *values[1] != 42) return 101; }
            return rc_live_count();
        }
        """);

    [Theory]
    [InlineData("int^()* create = &Create;")]
    [InlineData("int^()* create = () => Create();")]
    [InlineData("int^()* create = () => { return Create(); };")]
    public void IndirectReturnUsesCallerOwnedStorage(string declaration) => Succeeds("""
        int^ Create() => new^ int(42);
        int main() {
            DECLARATION
            { int^ value = create(); if (*value != 42) return 1; }
            return rc_live_count();
        }
        """.Replace("DECLARATION", declaration));

    [Theory]
    [InlineData("Base* source = &implementation;")]
    [InlineData("Factory* source = &implementation;")]
    public void VirtualAndInterfaceReturnsUseCallerOwnedStorage(string declaration) => Succeeds("""
        interface Factory { int^ Create(); }
        class Base { public virtual int^ Create() => new^ int(1); }
        class Derived : Base, Factory { public override int^ Create() => new^ int(42); }
        int main() {
            Derived implementation = new Derived();
            DECLARATION
            { int^ value = source.Create(); if (*value != 42) return 1; }
            return rc_live_count();
        }
        """.Replace("DECLARATION", declaration));

    [Fact]
    public void StructInterfaceReturnUsesCallerOwnedStorage() => Succeeds("""
        interface Factory { int^ Create(); }
        struct Concrete : Factory { public int^ Create() => new^ int(42); }
        int main() {
            Concrete implementation = new Concrete();
            Factory* source = &implementation;
            { int^ value = source.Create(); if (*value != 42) return 1; }
            return rc_live_count();
        }
        """);

    [Fact]
    public void InterfaceReferenceReturnPreservesBothWords() => Succeeds("""
        interface Value { int Read(); }
        struct Concrete : Value { public int Read() => 42; }
        Value^ Create() => new^ Concrete();
        int main() {
            { Value^ value = Create(); if (value.Read() != 42) return 1; }
            return rc_live_count();
        }
        """);

    [Fact]
    public void GenericReturnsHandleManagedValuesAndAggregates() => Succeeds("""
        struct Box<T> { public T item; public Box(T value) { item = value; } }
        T Identity<T>(T value) => value;
        int main() {
            {
                Box<int^> box = Identity(new Box<int^>(Identity(new^ int(42))));
                if (*box.item != 42) return 101;
            }
            return rc_live_count();
        }
        """);

    [Fact]
    public void NestedArgumentsAndSelfAssignmentKeepOwnership() => Succeeds("""
        int^ Create() => new^ int(21);
        int^ Identity(int^ value) => value;
        int Sum(int^ first, int^ second) => *first + *second;
        int main() {
            { int^ value = Identity(Create()); value = value; if (*value != 21) return 1; }
            if (Sum(Identity(Create()), Identity(Create())) != 42) return 2;
            return rc_live_count();
        }
        """);

    [Fact]
    public void OwningArgumentTransferKeepsItsManagedFieldsAliveAndReleasesCallerStorage() => Succeeds("""
        struct Owner {
            public int^ item;
            public Owner(int^ value) { item = value; }
            ~Owner() {}
        }
        int Consume(Owner value) => *value.item;
        int main() {
            if (Consume(new Owner(new^ int(42))) != 42) return 1;
            return rc_live_count();
        }
        """);

    [Fact]
    public void ReturnedValueSurvivesAllocatingDefers() => Succeeds("""
        int^ Create() {
            int^ value = new^ int(42);
            defer { int^ temporary = new^ int(7); }
            return value;
        }
        int main() {
            { int^ value = Create(); if (*value != 42) return 1; }
            return rc_live_count();
        }
        """);

    [Fact]
    public void ReusedResultSlotsDoNotReactivateRetiredReferences() => Succeeds("""
        int^ Create(int value) => new^ int(value);
        int main() {
            for (int i = 0; i < 10; i++) {
                int^ value = Create(i);
                if (*value != i) return 1;
            }
            return rc_live_count();
        }
        """);

    [Fact]
    public void NullableResultsCanReturnObjectsOrNull() => Succeeds("""
        int^? Create(bool present) { if (present) return new^ int(42); return null; }
        int main() {
            {
                int^? absent = Create(false);
                int^? present = Create(true);
                if (absent != null || present == null || *present! != 42) return 101;
            }
            return rc_live_count();
        }
        """);

    [Fact]
    public void ExceptionAfterResultStoreReleasesCallerStorage() => Succeeds("""
        int^ Create() {
            defer { throw new Exception("Failed during return cleanup"); }
            return new^ int(42);
        }
        int main() {
            try { int^ value = Create(); return 101; }
            catch (Exception* error) { }
            return rc_live_count();
        }
        """);

    [Fact]
    public void LoopConditionsAndDiscardedResultsReleaseTheirReferences() => Succeeds("""
        int^ Create(int value) => new^ int(value);
        int main() {
            int n = 0;
            while (*Create(n) < 5) {
                if (rc_live_count() != 0) return 101;
                n++;
            }
            for (int i = 0; *Create(i) < 5; i++) {
                Create(i);
                if (rc_live_count() != 0) return 102;
            }
            if (*Create(42) == 42) {
                if (rc_live_count() != 0) return 103;
            }
            return rc_live_count();
        }
        """);

    [Fact]
    public void NativeStructFieldsAreReleasedOnDelete() => Succeeds("""
        struct Box { public int^? item; }
        int main() {
            Box* box = new* Box();
            box.item = new^ int(42);
            if (*box.item! != 42) return 101;
            delete box;
            return rc_live_count();
        }
        """);

    [Fact]
    public void InterfaceDeleteReleasesImplicitManagedStructFields() => Succeeds("""
        interface Value { int Read(); }
        struct Box : Value {
            public int^ item;
            public Box(int^ value) { item = value; }
            public int Read() => *item;
        }
        int main() {
            Box* box = new* Box(new^ int(42));
            Value* view = box;
            if (view.Read() != 42) return 101;
            delete view;
            return rc_live_count();
        }
        """);

    [Fact]
    public void InheritedFieldsSurviveCopiesAndMoves() => Succeeds("""
        class Base { public int^? item; }
        class Derived : Base { public int extra; }
        int main() {
            {
                Derived value = new Derived();
                value.item = new^ int(42);
                Derived copied = value;
                Derived moved = move copied;
                if (*value.item! != 42 || *moved.item! != 42) return 101;
            }
            return rc_live_count();
        }
        """);

    [Fact]
    public void DerivedDestructorReleasesImplicitBaseFields() => Succeeds("""
        class Base { public int^? item; }
        class Derived : Base { ~Derived() { } }
        int main() {
            Derived* value = new* Derived();
            value.item = new^ int(42);
            delete value;
            return rc_live_count();
        }
        """);

    [Fact]
    public void MovedStructCanBeReinitializedWithoutLosingOtherCopies() => Succeeds("""
        struct Box { public int^ item; public Box(int^ value) { item = value; } }
        int main() {
            {
                Box value = new Box(new^ int(42));
                Box copied = value;
                Box moved = move value;
                value = new Box(new^ int(7));
                if (*copied.item != 42 || *moved.item != 42 || *value.item != 7) return 101;
            }
            return rc_live_count();
        }
        """);

    [Fact]
    public void NativeVectorOwnsElementsThroughGrowthPopClearAndDestruction() => Succeeds("""
        int main() {
            {
                std::Vector<int^> values = new std::Vector<int^>();
                for (int i = 0; i < 20; i++) values.Add(new^ int(i));
                if (rc_live_count() != 20) return 101;
                for (nuint i = 0; i < values.Count; i++) if (*values[i] != (int)i) return 102;
                { int^ last = values.Pop(); if (*last != 19) return 103; }
                if (rc_live_count() != 19) return 104;
                values.Clear();
                if (rc_live_count() != 0) return 105;
                values.Add(new^ int(42));
            }
            return rc_live_count();
        }
        """, vector: true);

    [Fact]
    public void LambdaParameterOwnsReferenceAfterCallerStorageChanges() => Succeeds("""
        int main() {
            {
                int^ source = new^ int(42);
                int(ref int^, int^)* read = (ref int^ slot, int^ value) => {
                    slot = new^ int(7);
                    if (rc_first_references() != 2) return 101;
                    return *value;
                };
                if (read(ref source, source) != 42 || *source != 7) return 102;
            }
            return rc_live_count();
        }
        """);

    [Fact]
    public void ManagedLambdaParameterCanBeReturnedAfterCallerStorageChanges() => Succeeds("""
        int main() {
            {
                int^ source = new^ int(42);
                int^(ref int^, int^)* take = (ref int^ slot, int^ value) => {
                    slot = new^ int(7);
                    return value;
                };
                int^ original = take(ref source, source);
                if (*original != 42 || *source != 7) return 101;
            }
            return rc_live_count();
        }
        """);
}
