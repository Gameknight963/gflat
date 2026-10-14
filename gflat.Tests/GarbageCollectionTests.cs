namespace gflat.Tests;

public sealed class GarbageCollectionTests
{
    private static ExecutionResult Run(string source, bool vector = false)
    {
        string[] names = vector ? ["libc/GC.gf", "core/Vector.gf"] : ["libc/GC.gf"];
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
    public void ReusedRootFramesDoNotRetainRetiredObjects() => Succeeds("""
        void Drop() { int^ value = new^ int(42); }
        int main() {
            for (int i = 0; i < 1200; i++) Drop();
            if (GC::ManagedCount != (nuint)1200) return 1;
            if (GC::Collect() != (nuint)1200) return 2;
            return 0;
        }
        """);

    [Fact]
    public void DeepFramesStayRootedBeyondMetadataCacheCapacity() => Succeeds("""
        void Recurse(int depth) {
            int^ value = new^ int(depth);
            if (depth != 0) Recurse(depth - 1);
            GC::Collect();
            if (*value != depth) throw new Exception("Lost recursive root");
        }
        int main() {
            Recurse(80);
            if (GC::ManagedCount != (nuint)1) return 1;
            if (GC::Collect() != (nuint)1) return 2;
            return 0;
        }
        """);

    [Fact]
    public void PublicAdoptionAndLiveScalarReferencesWork() => Succeeds("""
        struct Widget { public int value; public Widget(int n) { value = n; } }
        int main() {
            Widget* storage = new* Widget(42);
            Widget^ value = GC::Adopt(storage);
            int^ number = new^ int(7);
            GC::Collect();
            if (value.value != 42 || *number != 7) return 1;
            if (GC::ManagedCount != (nuint)2) return 2;
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
            if (GC::ManagedCount != (nuint)2) return 1;
            if (GC::Collect() != (nuint)2) return 2;
            if (GC::ManagedCount != (nuint)0) return 3;
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
            if (value.Read() != 42 || GC::ManagedCount != (nuint)1) return 1;
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
            if (Sum(&values) != 190 || GC::ManagedCount != (nuint)20) return 1;
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
        extern int puts(readonly(char*) text);
        namespace Memory {
            public replace void*? Allocate(nuint size) => malloc(size);
            public replace void Free(void* storage) { puts("freed"); free(storage); }
        }
        void Allocate() { int^ value = new^ int(42); }
        int main() { Allocate(); if (GC::Collect() != (nuint)1) return 1; return 0; }
        """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("freed\n", result.StandardOutput.Replace("\r\n", "\n"));
    }

    [Fact]
    public void AdoptionRejectsInteriorAndDuplicatePointersWithoutTakingOwnership() => Succeeds("""
        struct Widget { public int value; }
        int main() {
            Widget* storage = new* Widget();
            try { GC::Adopt(storage + 1); return 1; }
            catch (Exception* error) { }
            Widget^ managed = GC::Adopt(storage);
            try { GC::Adopt(storage); return 2; } catch (Exception* error) { }
            GC::Collect(); managed.value = 42;
            if (managed.value != 42) return 3;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructorOrReplacementAdoptionFailureFreesNewAllocation(bool adoptionFails)
    {
        string declaration = adoptionFails
            ? "namespace GC { public replace T^ Adopt<T>(T* storage) { throw new Exception(\"reject\"); } }"
            : "struct Widget { public int marker; public Widget() { marker = 42; throw new Exception(\"ctor\"); } }";
        string allocation = adoptionFails ? "int^ value = new^ int(42);" : "Widget^ value = new^ Widget();";
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
            """ + declaration + "int main() { try { " + allocation + " return 1; } catch (Exception* error) { } return 0; }");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("released object\n", result.StandardOutput.Replace("\r\n", "\n"));
    }

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
                    if (size > (nuint)4) return null;
                    return malloc(size);
                }
                public replace void Free(void* storage) { free(storage); }
            }
            class ManagedError : Exception {
                public int^ value;
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
    public void ManagedAllocationFailureThrowsBadAllocWithoutAnotherSuccessfulAllocation() => Succeeds("""
        namespace Memory {
            public replace void*? Allocate(nuint size) => null;
            public replace void Free(void* storage) { }
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
            if (GC::ManagedCount != (nuint)1 || GC::ManagedBytes != (nuint)1) return 1;
            return 0;
        }
        """);

    [Fact]
    public void LegacyAllocationHookTakesPrecedenceWhenCollectorSourcesArePresent()
    {
        var result = Run("""
            extern void*? malloc(nuint size);
            extern int puts(readonly(char*) text);
            void*? __gflat_gc_alloc(nuint size) { puts("legacy allocation"); return malloc(size); }
            int main() {
                int^ value = new^ int(42);
                if (*value != 42 || GC::ManagedCount != (nuint)0) return 1;
                return 0;
            }
            """);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("legacy allocation\n", result.StandardOutput.Replace("\r\n", "\n"));
    }

    [Theory]
    [InlineData("int EnterFrame() nothrow => 0;")]
    [InlineData("void* EnterFrame() => null;")]
    public void InvalidCollectorInfrastructureHookIsDiagnosed(string hook)
    {
        string source = "namespace GC { public weak T^ Adopt<T>(T* value) { throw new Exception(\"unused\"); } private weak " + hook + " } int main() => 0;";
        var error = Assert.Throws<CompileExceptions.TypeCheckException>(() => Compiler.Check(source));
        Assert.Contains("GC", error.Message);
    }

    [Fact]
    public void AdoptionBudgetAutomaticallyCollectsDeadObjectsAndPreservesLiveReferences() => Succeeds("""
        namespace GC { public replace nuint CollectionThreshold() => 1; }
        void AllocateDead() { int^ value = new^ int(42); }
        int main() {
            AllocateDead();
            if (GC::ManagedCount != (nuint)1) return 1;
            int^ live = new^ int(7);
            if (GC::ManagedCount != (nuint)1 || *live != 7) return 2;
            int^ another = new^ int(9);
            if (GC::ManagedCount != (nuint)2 || *live != 7 || *another != 9) return 3;
            return 0;
        }
        """);

    [Fact]
    public void ZeroBudgetDisablesAutomaticCollectionButExplicitCollectionWorks() => Succeeds("""
        namespace GC { public replace nuint CollectionThreshold() => 0; }
        void Allocate(int n) { int^ value = new^ int(n); }
        int main() {
            Allocate(1); Allocate(2);
            if (GC::ManagedCount != (nuint)2) return 1;
            if (GC::Collect() != (nuint)2 || GC::ManagedCount != (nuint)0) return 2;
            return 0;
        }
        """);

    [Fact]
    public void AllocationFailureCollectsAndRetriesOnce() => Succeeds("""
        extern void*? malloc(nuint size);
        extern void free(void* storage);
        namespace Memory {
            public replace void*? Allocate(nuint size) {
                if (size == (nuint)4 && GC::ManagedCount != (nuint)0) return null;
                return malloc(size);
            }
            public replace void Free(void* storage) { free(storage); }
        }
        void Drop() { int^ value = new^ int(42); }
        int main() {
            Drop();
            int^ value = new^ int(7);
            if (*value != 7 || GC::ManagedCount != (nuint)1) return 1;
            return 0;
        }
        """);

    [Fact]
    public void MerelyIncludingGcDoesNotInstrumentRawOnlyPrograms()
    {
        var sources = new[] {
            new SourceFile("libc/GC.gf", System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", "libc", "GC.gf"))),
            new SourceFile("main.gf", "int main() => 0;")
        };
        Assert.DoesNotContain("call i8* @gflat$GC$EnterFrame", Compiler.Emit(sources));
    }

    [Fact]
    public void PrivateGcRuntimeBridgeHasNoMandatoryNativeSymbolName()
    {
        string source = """
            namespace GC {
                private extern void^? RuntimeReference(void* address, nuint bytes);
                public int^? Bridge(void* address) { return (int^?)RuntimeReference(address, 4); }
            }
            int main() { void*? storage = Memory::Allocate(4); if (storage == null) return 1; int^? value = GC::Bridge(storage!); return 0; }
            """;
        var sources = new[] {
            new SourceFile("libc/GC.gf", System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", "libc", "GC.gf"))),
            new SourceFile("main.gf", source)
        };
        string ir = Compiler.Emit(sources);
        Assert.Contains("@RuntimeReference", ir);
        Assert.Contains("call i8* @gflat$GC$EnterFrame", ir);
    }

    [Fact]
    public void RawAliasCannotManuallyFreeCollectorOwnedAllocation()
    {
        var result = Run("""
            struct Widget { public int value; }
            int main() {
                Widget* storage = new* Widget();
                Widget^ value = GC::Adopt(storage);
                Memory::Free((void*)storage);
                return 0;
            }
            """);
        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public void RawMethodReceiverCannotDeleteManagedObject()
    {
        var result = Run("""
            class Widget { public void Destroy() { delete this; } }
            int main() { Widget^ value = new^ Widget(); value.Destroy(); return 0; }
            """);
        Assert.NotEqual(0, result.ExitCode);
    }
}
