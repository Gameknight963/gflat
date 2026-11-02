namespace Standard;

// Represents the core contract a garbage collector must implement.
// Managed references use allocation and slot notifications. Completed objects
// with destructors also register their ordinary destruction callback.
public static interface IGarbageCollector
{
    void* Allocate(nuint bytes, nuint alignment);
    void ReferenceAdded(void* slot) nothrow;
    void ReferenceRemoved(void* slot) nothrow;
    void RegisterDestructor(void* storage, void(void*)* destroy) nothrow;
    nuint Collect();
}
