// Non-moving collector hooks. Adopt transfers allocation ownership on success.
// Raw aliases must no longer free the allocation after adoption.
namespace GC;

private extern void^? __gflat_gc_adopt_reference(void* storage, nuint bytes);
private extern nuint __gflat_gc_collect(void(void*)* release);
private extern nuint __gflat_gc_managed_count();
private extern nuint __gflat_gc_managed_bytes();
private extern bool __gflat_gc_should_collect(nuint threshold);
private extern bool __gflat_gc_track_native(void* address, nuint bytes);
private extern void __gflat_gc_forget_native(void* address);
private extern bool __gflat_gc_prepare_allocation(void* address, nuint bytes);
private extern void __gflat_gc_reference_added(void* slot);
private extern void __gflat_gc_reference_removed(void* slot);

// Storage is registered and zeroed before construction. Memory's allocator is
// malloc-like: an unsatisfied alignment request is an allocation failure.
public weak void* Allocate(nuint bytes, nuint alignment)
{
    if (bytes == (nuint)0) bytes = 1;
    if (alignment == (nuint)0) throw new std::BadAllocException();
    if (__gflat_gc_should_collect(CollectionThreshold())) Collect();
    void*? storage = Memory::Allocate(bytes);
    if (storage == null) { Collect(); storage = Memory::Allocate(bytes); }
    if (storage == null) throw new std::BadAllocException();
    if ((nuint)storage % alignment != (nuint)0)
    {
        Memory::Free(storage);
        throw new std::BadAllocException();
    }
    if (!__gflat_gc_prepare_allocation(storage, bytes))
    {
        Memory::Free(storage);
        throw new std::BadAllocException();
    }
    return storage;
}

// These identify initialized managed-reference slots, not the target objects.
// They must neither collect nor allocate managed storage, and must not throw.
private weak void ReferenceAdded(void* slot) nothrow => __gflat_gc_reference_added(slot);
private weak void ReferenceRemoved(void* slot) nothrow => __gflat_gc_reference_removed(slot);

// Native allocation bookkeeping supports adoption and retiring freed buffer slots.
private weak bool TrackNative(void* address, nuint bytes) nothrow => __gflat_gc_track_native(address, bytes);
private weak void ForgetNative(void* address) nothrow => __gflat_gc_forget_native(address);

// Only complete, destructor-free objects in Memory allocations are eligible.
public weak T^ Adopt<T>(T* storage)
{
    nuint stride = (nuint)sizeof(T);
    if (stride == (nuint)0) stride = 1;
    // Storage still belongs to the scanned native heap while collection runs.
    if (__gflat_gc_should_collect(CollectionThreshold())) Collect();
    void^? result = __gflat_gc_adopt_reference((void*)storage, stride);
    if (result == null) throw new Exception("GC adoption failed");
    return (T^)result;
}

public weak nuint Collect() => __gflat_gc_collect(&Memory::Free);
// Bytes adopted since the previous collection. Zero disables automatic collection.
public weak nuint CollectionThreshold() => 65536;
public nuint ManagedCount => __gflat_gc_managed_count();
public nuint ManagedBytes => __gflat_gc_managed_bytes();
