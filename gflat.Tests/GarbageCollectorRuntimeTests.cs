namespace gflat.Tests;

// Exercise the provider directly, independently of compiler-inserted slot calls.
// Compiler lifetime tests separately cover the same provider through new^.
public sealed class GarbageCollectorRuntimeTests
{
    private static void Succeeds(string body, string declarations = "")
    {
        var result = CompilerTestHelper.Run([
            new SourceFile("Standard/core/IGarbageCollector.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", "core", "IGarbageCollector.gf"))),
            new SourceFile("Standard/libc/TracingCollector.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", "libc", "TracingCollector.gf"))),
            new SourceFile("Standard/libc/GC.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Standard", "libc", "GC.gf"))),
            new SourceFile("main.gf", declarations + "alias Collector = Standard::TracingCollector; int main() { " + body + " }")
        ]);
        Assert.True(result.ExitCode == 0, $"Exit {result.ExitCode}: {result.StandardError}");
    }

    [Fact]
    public void InitializedRootSlotsSurviveAndRepeatedNotificationsAreIdempotent() => Succeeds("""
        void* root = Collector::Allocate(32, 8);
        Collector::ReferenceAdded(&root);
        Collector::ReferenceAdded(&root);
        if (Collector::Collect() != (nuint)0) return 1;
        Collector::ReferenceRemoved(&root);
        Collector::ReferenceRemoved(&root);
        if (Collector::Collect() != (nuint)1 || Collector::ManagedCount != (nuint)0) return 2;
        return 0;
        """);

    [Fact]
    public void RawNativeBytesDoNotBecomeManagedRoots() => Succeeds("""
        void* object = Collector::Allocate(32, 8);
        void** native = (void**)Memory::Allocate((nuint)sizeof(void*));
        *native = object;
        if (Collector::Collect() != (nuint)1) return 1;
        Memory::Free(native);
        return 0;
        """);

    [Fact]
    public void ManagedSlotsTraceEdgesWithoutRootingUnreachableCycles() => Succeeds("""
        void** first = (void**)Collector::Allocate((nuint)sizeof(void*), (nuint)alignof(void*));
        void** second = (void**)Collector::Allocate((nuint)sizeof(void*), (nuint)alignof(void*));
        if ((*first).reinterpret<nuint>() != (nuint)0 || (*second).reinterpret<nuint>() != (nuint)0) return 1;
        *first = second;
        *second = first;
        Collector::ReferenceAdded(first);
        Collector::ReferenceAdded(second);
        Collector::ReferenceAdded(&first);
        if (Collector::Collect() != (nuint)0) return 2;
        Collector::ReferenceRemoved(&first);
        if (Collector::Collect() != (nuint)2) return 3;
        Collector::Allocate((nuint)sizeof(void*), (nuint)alignof(void*));
        if (Collector::Collect() != (nuint)1) return 4;
        return 0;
        """);

    [Fact]
    public void NativeBuffersExplicitlyRetireTheirRegisteredSlots() => Succeeds("""
        void** native = (void**)Memory::Allocate((nuint)sizeof(void*));
        *native = Collector::Allocate(32, 8);
        Collector::ReferenceAdded(native);
        if (Collector::Collect() != (nuint)0) return 1;
        Collector::ReferenceRemoved(native);
        Memory::Free(native);
        if (Collector::Collect() != (nuint)1 || Collector::ManagedCount != (nuint)0) return 2;
        return 0;
        """);

    [Fact]
    public void ReassigningARegisteredSlotRequiresNoNotification() => Succeeds("""
        void* root = Collector::Allocate(16, 8);
        Collector::ReferenceAdded(&root);
        root = Collector::Allocate(24, 8);
        if (Collector::Collect() != (nuint)1 || Collector::ManagedBytes != (nuint)24) return 1;
        Collector::ReferenceRemoved(&root);
        if (Collector::Collect() != (nuint)1) return 2;
        return 0;
        """);

    [Fact]
    public void InteriorManagedReferenceKeepsItsAllocationAlive() => Succeeds("""
        byte* storage = (byte*)Collector::Allocate(32, 8);
        byte* interior = storage + 7;
        Collector::ReferenceAdded(&interior);
        if (Collector::Collect() != (nuint)0) return 1;
        Collector::ReferenceRemoved(&interior);
        if (Collector::Collect() != (nuint)1) return 2;
        return 0;
        """);

    [Fact]
    public void ResizedRegistryPreservesRootsThroughRemovalAndAddressReuse() => Succeeds("""
        void** roots = (void**)Memory::Allocate((nuint)160 * (nuint)sizeof(void*))!;
        for (int i = 0; i < 160; i++) {
            roots[i] = Collector::Allocate(8, 8);
            *(int*)roots[i] = i;
            Collector::ReferenceAdded(&roots[i]);
        }
        for (int i = 0; i < 160; i++) Collector::ReferenceAdded(&roots[i]);
        if (Collector::Collect() != 0) return 1;
        for (int i = 0; i < 160; i += 2) {
            Collector::ReferenceRemoved(&roots[i]);
            Collector::ReferenceRemoved(&roots[i]);
        }
        if (Collector::Collect() != 80) return 2;
        for (int i = 1; i < 160; i += 2) if (*(int*)roots[i] != i) return 3;
        for (int i = 0; i < 160; i += 2) {
            roots[i] = Collector::Allocate(8, 8);
            Collector::ReferenceAdded(&roots[i]);
        }
        if (Collector::Collect() != 0 || Collector::ManagedCount != 160) return 4;
        for (int i = 159; i >= 0; i--) Collector::ReferenceRemoved(&roots[i]);
        if (Collector::Collect() != 160 || Collector::ManagedCount != 0) return 5;
        Memory::Free(roots);
        return 0;
        """);

    private const string AllocationProbe = """
        namespace probe_native {
            extern void*? malloc(nuint size);
            extern void free(void* address);
        }
        static class AllocationProbe {
            public static nuint Allocations;
            public static nuint Frees;
        }
        namespace Memory {
            public replace void*? Allocate(nuint size) {
                AllocationProbe::Allocations++;
                return probe_native::malloc(size);
            }
            public replace void Free(void* address) nothrow {
                AllocationProbe::Frees++;
                probe_native::free(address);
            }
        }
        """;

    [Fact]
    public void RepeatedRegistrationReusesBookkeepingWithoutAllocating() => Succeeds("""
        void* root = Collector::Allocate(32, 8);
        Collector::ReferenceAdded(&root);
        Collector::ReferenceRemoved(&root);
        nuint before = AllocationProbe::Allocations;
        nuint freed = AllocationProbe::Frees;
        for (int i = 0; i < 1000; i++) {
            Collector::ReferenceAdded(&root);
            if (i % 100 == 0 && Collector::Collect() != 0) return 1;
            Collector::ReferenceRemoved(&root);
        }
        if (AllocationProbe::Allocations != before || AllocationProbe::Frees != freed) return 2;
        if (Collector::Collect() != 1) return 3;
        return 0;
        """, AllocationProbe);

    [Fact]
    public void BookkeepingCacheDoesNotRetainAnUnboundedRegistrationBurst() => Succeeds("""
        void** roots = (void**)Memory::Allocate((nuint)512 * (nuint)sizeof(void*))!;
        for (int i = 0; i < 512; i++) {
            roots[i] = ((nuint)1).reinterpret<void*>();
            Collector::ReferenceAdded(&roots[i]);
        }
        nuint before = AllocationProbe::Frees;
        for (int i = 0; i < 512; i++) Collector::ReferenceRemoved(&roots[i]);
        if (AllocationProbe::Frees - before < 256) return 1;
        Memory::Free(roots);
        return 0;
        """, AllocationProbe);
}
