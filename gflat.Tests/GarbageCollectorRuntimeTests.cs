namespace gflat.Tests;

// Exercise the provider directly, independently of compiler-inserted slot calls.
// Compiler lifetime tests separately cover the same provider through new^.
public sealed class GarbageCollectorRuntimeTests
{
    private static void Succeeds(string body)
    {
        var result = CompilerTestHelper.Run([
            new SourceFile("std/core/GarbageCollector.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", "core", "GarbageCollector.gf"))),
            new SourceFile("std/libc/GC.gf", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std", "libc", "GC.gf"))),
            new SourceFile("main.gf", "alias Collector = std::TracingCollector; int main() { " + body + " }")
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
        if ((nuint)*first != (nuint)0 || (nuint)*second != (nuint)0) return 1;
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
}
