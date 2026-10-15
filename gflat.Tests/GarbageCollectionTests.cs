namespace gflat.Tests;

public sealed class GarbageCollectionTests
{
    private static ExecutionResult Run(string source, bool vector = false)
    {
        string[] names = vector ? ["libc/GC.gf", "libc/TracingCollector.gf", "core/Vector.gf", "core/Span.gf"] : ["libc/GC.gf", "libc/TracingCollector.gf"];
        return CompilerTestHelper.Run(names.Select(name => new SourceFile(name,
            System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", name))))
            .Append(new SourceFile("main.gf", source)));
    }

    private static void Succeeds(string source, bool vector = false)
    {
        var result = Run(source, vector);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Fact]
    public void ReusedReferenceSlotsDoNotRetainRetiredObjects() => Succeeds("""
        void Drop() { int^ value = new^ int(42); }
        int main() {
            for (int i = 0; i < 1200; i++) Drop();
            if (std::TracingCollector::ManagedCount != (nuint)1200) return 1;
            if (GC::Collect() != (nuint)1200) return 2;
            return 0;
        }
        """);

    [Fact]
    public void RecursiveCallsKeepTheirLiveReferencesRooted() => Succeeds("""
        void Recurse(int depth) {
            int^ value = new^ int(depth);
            if (depth != 0) Recurse(depth - 1);
            GC::Collect();
            if (*value != depth) throw new Exception("Lost recursive root");
        }
        int main() {
            Recurse(80);
            if (std::TracingCollector::ManagedCount != (nuint)1) return 1;
            if (GC::Collect() != (nuint)1) return 2;
            return 0;
        }
        """);

    [Fact]
    public void DirectManagedAllocationActivatesRuntimeTracking() => Succeeds("""
        int main() {
            void* storage = GC::Allocate(4, 4);
            if (std::TracingCollector::ManagedCount != (nuint)1) return 1;
            if (GC::Collect() != (nuint)1) return 2;
            return 0;
        }
        """);

    [Fact]
    public void PublicAdoptionAndLiveScalarReferencesWork() => Succeeds("""
        struct Widget { public int value; public Widget(int n) { value = n; } }
        int main() {
            Widget* storage = new* Widget(42);
            Widget^ value = std::TracingCollector::Adopt(storage);
            int^ number = new^ int(7);
            GC::Collect();
            if (value.value != 42 || *number != 7) return 1;
            if (std::TracingCollector::ManagedCount != (nuint)2) return 2;
            return 0;
        }
        """);

    [Fact]
    public void UnreachableObjectsAndCyclesAreCollected() => Succeeds("""
        class Node { public Node^? next; }
        void CreateCycle() {
            Node^ a = new^ Node(); Node^ b = new^ Node();
            a.next = b; b.next = a;
        }
        int main() {
            CreateCycle();
            if (std::TracingCollector::ManagedCount != (nuint)2) return 1;
            if (GC::Collect() != (nuint)2) return 2;
            if (std::TracingCollector::ManagedCount != (nuint)0) return 3;
            return 0;
        }
        """);

    [Fact]
    public void ConstructorAndArgumentEvaluationKeepTemporaryObjectsAlive() => Succeeds("""
        class Box {
            public int value;
            public Box(int n) { GC::Collect(); value = n; }
        }
        int CollectAndReturn() { GC::Collect(); return 5; }
        int Sum(Box^ box, int n) { GC::Collect(); return box.value + n; }
        int main() { if (Sum(new^ Box(37), CollectAndReturn()) != 42) return 1; return 0; }
        """);

    [Fact]
    public void ExceptionUnwindingRemovesDeadRootFrames() => Succeeds("""
        void Fail() { int^ number = new^ int(42); throw new Exception("leave"); }
        int main() {
            try { Fail(); } catch (Exception* error) { }
            if (GC::Collect() != (nuint)1) return 1;
            return 0;
        }
        """);

    [Fact]
    public void ManagedInterfaceReferenceKeepsItsObjectAlive() => Succeeds("""
        interface IValue { int Read(); }
        class Box : IValue {
            public int value;
            public Box(int n) { value = n; }
            public int Read() { GC::Collect(); return value; }
        }
        IValue^ Create() { return new^ Box(42); }
        int main() {
            IValue^ value = Create(); GC::Collect();
            if (value.Read() != 42 || std::TracingCollector::ManagedCount != (nuint)1) return 1;
            return 0;
        }
        """);

    [Fact]
    public void NativeAllocationContainingManagedReferencesKeepsThemAlive() => Succeeds("""
        struct Holder { public int^? value; }
        Holder* Fill() {
            Holder* holder = new* Holder(); holder.value = new^ int(42); return holder;
        }
        int ReadAndDelete(Holder* holder) {
            int result = *holder.value!;
            delete holder;
            return result;
        }
        int main() {
            Holder* holder = Fill(); GC::Collect();
            if (ReadAndDelete(holder) != 42) return 1;
            if (GC::Collect() != (nuint)1) return 2;
            return 0;
        }
        """);

    [Fact]
    public void NativeVectorGrowthAndClearTrackManagedElements() => Succeeds("""
        using std;
        void Fill(Vector<int^>* values) {
            for (int i = 0; i < 20; i++) values.Add(new^ int(i));
        }
        int Sum(Vector<int^>* values) {
            int sum = 0;
            for (nuint i = 0; i < values.Count; i++) sum += *(*values)[i];
            return sum;
        }
        int main() {
            Vector<int^> values = new Vector<int^>(); Fill(&values);
            GC::Collect();
            if (Sum(&values) != 190 || std::TracingCollector::ManagedCount != (nuint)20) return 1;
            values.Clear();
            if (GC::Collect() != (nuint)20) return 2;
            return 0;
        }
        """, vector: true);

    [Fact]
    public void NativeVectorPopDoesNotRetainRemovedManagedReference() => Succeeds("""
        using std;
        void Fill(Vector<int^>* values) { values.Add(new^ int(42)); }
        int Take(Vector<int^>* values) { int^ value = values.Pop(); return *value; }
        int main() {
            Vector<int^> values = new Vector<int^>(); Fill(&values);
            if (Take(&values) != 42) return 1;
            if (GC::Collect() != (nuint)1) return 2;
            return 0;
        }
        """, vector: true);

    [Fact]
    public void ReplacedMemoryHooksStillTrackAndFreeManagedAllocations()
    {
        var result = Run("""
        extern void*? malloc(nuint size);
        extern void free(void* storage);
        extern int printf(readonly(char*) text, ...);
        namespace Memory {
            public replace void*? Allocate(nuint size) { printf("A"); return malloc(size); }
            public replace void Free(void* storage) { printf("F"); free(storage); }
        }
        void Allocate() { int^ value = new^ int(42); }
        int main() {
            Allocate();
            if (GC::Collect() != (nuint)1 || std::TracingCollector::ManagedCount != (nuint)0) return 1;
            return 0;
        }
        """);
        Assert.Equal(0, result.ExitCode);
        // The collector keeps one process-lifetime State allocation. Object and
        // root bookkeeping allocations must all be freed through the same hooks.
        Assert.True(result.StandardOutput.Count(c => c == 'F') > 0);
        Assert.Equal(result.StandardOutput.Count(c => c == 'A') - 1,
            result.StandardOutput.Count(c => c == 'F'));
    }

    [Fact]
    public void AdoptionRejectsDuplicateAndOverlappingManagedPointersWithoutTakingOwnership() => Succeeds("""
        struct Widget { public int value; }
        int main() {
            Widget* storage = new* Widget();
            Widget^ managedValue = std::TracingCollector::Adopt(storage);
            try { std::TracingCollector::Adopt(storage); return 2; } catch (Exception* error) { }
            try { std::TracingCollector::Adopt((char*)storage + 1); return 1; }
            catch (Exception* error) { }
            GC::Collect(); managedValue.value = 42;
            if (managedValue.value != 42) return 3;
            return 0;
        }
        """);

    [Theory]
    [InlineData("extern int^ Read();")]
    [InlineData("extern void Save(int^ value);")]
    [InlineData("extern void Save(int^* value);")]
    [InlineData("struct Holder { public int^? value; } extern Holder Read();")]
    [InlineData("struct Holder { public int^? value; } extern void Save(Holder* value);")]
    public void ManagedReferencesCannotCrossOrdinaryNativeDeclarations(string source)
    {
        var error = Assert.Throws<CompileExceptions.TypeCheckException>(() => Compiler.Check(source));
        Assert.Contains("managed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailedManagedConstructionIsReclaimedByCollector()
    {
        var result = Run("""
            extern void*? malloc(nuint size);
            extern void free(void* storage);
            extern int puts(readonly(char*) text);
            namespace Memory {
                public replace void*? Allocate(nuint size) => malloc(size);
                public replace void Free(void* storage) {
                    if (*((int*)storage) == 42) puts("released object");
                    free(storage);
                }
            }
            struct Widget { public int marker; public Widget() { marker = 42; throw new Exception("ctor"); } }
            int main() {
                try { Widget^ value = new^ Widget(); return 1; } catch (Exception* error) { }
                if (std::TracingCollector::ManagedCount != (nuint)1) return 2;
                if (GC::Collect() != (nuint)1) return 3;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("released object\n", result.StandardOutput.Replace("\r\n", "\n"));
    }

    [Fact]
    public void NewManagedAllocationDoesNotCallAdopt() => Succeeds("""
        int main() {
            int^ value = new^ int(42);
            if (*value != 42) return 1;
            return 0;
        }
        """);

    [Fact]
    public void StandardLibraryManagedGraphExampleExecutes()
    {
        string project = Path.Combine(TestSupport.ProjectFileFixture.RepositoryRoot,
            "examples", "managed", "managed.gfproj");
        var graph = Projects.ProjectGraph.Load(project, path => Projects.ProjectModel.Load(path));
        var result = CompilerTestHelper.Run(graph.Sources.Select(path => new SourceFile(path, System.IO.File.ReadAllText(path))));
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("20 cyclic nodes survive through a native Vector.\nRemoving the roots reclaims all 20 cycles.\n",
            result.StandardOutput.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ExceptionReserveStorageKeepsManagedFieldsAlive()
    {
        Succeeds("""
            extern void*? malloc(nuint size);
            extern void free(void* storage);
            namespace Memory {
                public replace void*? Allocate(nuint size) {
                    if (size == (nuint)sizeof(ManagedError)) return null;
                    return malloc(size);
                }
                public replace void Free(void* storage) { free(storage); }
            }
            class ManagedError : Exception {
                public int^ value;
                // Distinguish exception payload failure from the collector's
                // small State/Slot bookkeeping allocations through Memory.
                private byte[256] padding;
                public ManagedError(int^ item) : base("managed error", 1) { value = item; }
            }
            void Fail() { throw new ManagedError(new^ int(42)); }
            int Handle() {
                try { Fail(); } catch (ManagedError* error) { GC::Collect(); return *error.value; }
                return 0;
            }
            int main() {
                if (Handle() != 42) return 1;
                if (GC::Collect() != (nuint)1) return 2;
                return 0;
            }
            """);
    }

    [Fact]
    public void ManagedPayloadAndExceptionAllocationFailureUsesBadAllocReserve() => Succeeds("""
        extern void*? malloc(nuint size);
        extern void free(void* storage);
        namespace Memory {
            public replace void*? Allocate(nuint size) {
                if (size == (nuint)sizeof(int) || size == (nuint)sizeof(std::BadAllocException)) return null;
                return malloc(size);
            }
            public replace void Free(void* storage) { free(storage); }
        }
        int main() {
            try { int^ value = new^ int(42); return 1; }
            catch (std::BadAllocException* error) { return 0; }
        }
        """);

    [Fact]
    public void EmptyManagedObjectsHaveNonzeroOwnedStorage() => Succeeds("""
        struct Empty { }
        int main() {
            Empty^ value = new^ Empty(); GC::Collect();
            if (std::TracingCollector::ManagedCount != (nuint)1 || std::TracingCollector::ManagedBytes != (nuint)1) return 1;
            return 0;
        }
        """);

    [Fact]
    public void LegacyAllocationSymbolDoesNotOverrideSelectedCollector()
    {
        var result = Run("""
            extern void*? malloc(nuint size);
            extern int puts(readonly(char*) text);
            void*? __gflat_gc_alloc(nuint size) { puts("legacy allocation"); return malloc(size); }
            int main() {
                int^ value = new^ int(42);
                if (*value != 42 || std::TracingCollector::ManagedCount != (nuint)1) return 1;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
    }

    [Theory]
    [InlineData("int ReferenceAdded(void* slot) nothrow => 0;")]
    [InlineData("void ReferenceAdded(void* slot) {}")]
    public void InvalidCollectorInfrastructureHookIsDiagnosed(string hook)
    {
        string source = "static class Broken : std::IGarbageCollector { public static void* Allocate(nuint bytes, nuint alignment) => (void*)1; public static void ReferenceRemoved(void* slot) nothrow {} public static nuint Collect() => 0; public static " + hook + " } alias GC = Broken; int main() => 0;";
        var error = Assert.Throws<CompileExceptions.TypeCheckException>(() => Compiler.Check(source));
        Assert.Contains("ReferenceAdded", error.Message);
    }

    [Fact]
    public void AdoptionBudgetAutomaticallyCollectsDeadObjectsAndPreservesLiveReferences() => Succeeds("""
        public static replace nuint std::TracingCollector::CollectionThreshold() => 1;
        void AllocateDead() { int^ value = new^ int(42); }
        int main() {
            AllocateDead();
            if (std::TracingCollector::ManagedCount != (nuint)1) return 1;
            int^ live = new^ int(7);
            if (std::TracingCollector::ManagedCount != (nuint)1 || *live != 7) return 2;
            int^ another = new^ int(9);
            if (std::TracingCollector::ManagedCount != (nuint)2 || *live != 7 || *another != 9) return 3;
            return 0;
        }
        """);

    [Fact]
    public void ZeroBudgetDisablesAutomaticCollectionButExplicitCollectionWorks() => Succeeds("""
        public static replace nuint std::TracingCollector::CollectionThreshold() => 0;
        void Allocate(int n) { int^ value = new^ int(n); }
        int main() {
            Allocate(1); Allocate(2);
            if (std::TracingCollector::ManagedCount != (nuint)2) return 1;
            if (GC::Collect() != (nuint)2 || std::TracingCollector::ManagedCount != (nuint)0) return 2;
            return 0;
        }
        """);

    [Fact]
    public void AllocationFailureCollectsAndRetriesOnce() => Succeeds("""
        extern void*? malloc(nuint size);
        extern void free(void* storage);
        namespace Memory {
            public replace void*? Allocate(nuint size) {
                if (size == (nuint)4 && std::TracingCollector::ManagedCount != (nuint)0) return null;
                return malloc(size);
            }
            public replace void Free(void* storage) { free(storage); }
        }
        void Drop() { int^ value = new^ int(42); }
        int main() {
            Drop();
            int^ value = new^ int(7);
            if (*value != 7 || std::TracingCollector::ManagedCount != (nuint)1) return 1;
            return 0;
        }
        """);

    [Fact]
    public void MerelyIncludingGcDoesNotInstrumentRawOnlyPrograms()
    {
        var sources = new[] {
            new SourceFile("std/libc/TracingCollector.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", "libc", "TracingCollector.gf"))),
            new SourceFile("libc/GC.gf", System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", "libc", "GC.gf"))),
            new SourceFile("main.gf", "int main() => 0;")
        };
        Assert.DoesNotContain("call i8* @gflat$GC$EnterFrame", Compiler.Emit(sources));
    }

    [Fact]
    public void PrivateGcRuntimeBridgeHasNoMandatoryNativeSymbolName()
    {
        string source = """
            private extern void*? RuntimeReference(void* address, nuint bytes);
            public int^? Bridge(void* address) { return (int^?)RuntimeReference(address, 4); }
            int main() { void*? storage = Memory::Allocate(4); if (storage == null) return 1; int^? value = Bridge(storage!); return 0; }
            """;
        var sources = new[] {
            new SourceFile("std/libc/TracingCollector.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", "libc", "TracingCollector.gf"))),
            new SourceFile("libc/GC.gf", System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", "libc", "GC.gf"))),
            new SourceFile("main.gf", source)
        };
        string ir = Compiler.Emit(sources);
        Assert.Contains("@RuntimeReference", ir);
        Assert.Contains("ReferenceAdded", ir);
        Assert.DoesNotContain("call i8* @gflat$GC$EnterFrame", ir);
    }

    [Fact]
    public void RawMemoryFreeHasNoCollectorInterception()
    {
        var sources = new[] {
            new SourceFile("std/libc/TracingCollector.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", "libc", "TracingCollector.gf"))),
            new SourceFile("std/libc/GC.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", "libc", "GC.gf"))),
            new SourceFile("main.gf", "void FreeRaw(void* storage) { Memory::Free(storage); } int main() => 0;")
        };
        string ir = Compiler.Emit(sources);
        Assert.DoesNotContain("ForgetNative", ir);
        Assert.DoesNotContain("TrackNative", ir);
    }

    [Fact]
    public void AdoptingRawStorageDoesNotAddCompilerHookDependencies()
    {
        var sources = new[] {
            new SourceFile("std/libc/TracingCollector.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", "libc", "TracingCollector.gf"))),
            new SourceFile("std/libc/GC.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", "libc", "GC.gf"))),
            new SourceFile("main.gf", "int main() { int* storage = (int*)Memory::Allocate(4); *storage = 42; int^ value = std::TracingCollector::Adopt(storage); return *value; }")
        };
        string ir = Compiler.Emit(sources);
        Assert.DoesNotContain("__gflat_gc_", ir);
        Assert.DoesNotContain("EnterFrame", ir);
    }
}
