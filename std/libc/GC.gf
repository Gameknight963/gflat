// Non-moving collector hooks. Adopt transfers allocation ownership on success.
// Raw aliases must no longer free the allocation after adoption.
namespace GC;

private extern void^? __gflat_gc_adopt_reference(void* storage, nuint bytes);
private extern nuint __gflat_gc_collect(void(void*)* release);
private extern nuint __gflat_gc_managed_count();
private extern nuint __gflat_gc_managed_bytes();
private extern bool __gflat_gc_should_collect(nuint threshold);
private extern void* __gflat_gc_frame_enter();
private extern void __gflat_gc_frame_add(void* frame, void* address, nuint bytes);
private extern void __gflat_gc_frame_leave(void* frame);
private extern bool __gflat_gc_track_native(void* address, nuint bytes);
private extern void __gflat_gc_forget_native(void* address);

// Compiler-facing hooks are replaceable as a set with Adopt and Collect.
// They must not throw. Root frame storage remains valid until LeaveFrame.
private weak void* EnterFrame() nothrow => __gflat_gc_frame_enter();
private weak void AddRoot(void* frame, void* address, nuint bytes) nothrow => __gflat_gc_frame_add(frame, address, bytes);
private weak void LeaveFrame(void* frame) nothrow => __gflat_gc_frame_leave(frame);
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
    return (T^)result!;
}

public weak nuint Collect() => __gflat_gc_collect(&Memory::Free);
// Bytes adopted since the previous collection. Zero disables automatic collection.
public weak nuint CollectionThreshold() => 65536;
public nuint ManagedCount => __gflat_gc_managed_count();
public nuint ManagedBytes => __gflat_gc_managed_bytes();
