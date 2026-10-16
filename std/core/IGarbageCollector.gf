namespace std;

// Represents the core contract a garbage collector must implement.
// Managed references require the first three operations. Collect is a common
// library operation; the compiler never inserts collection requests itself.
public static interface IGarbageCollector
{
    void* Allocate(nuint bytes, nuint alignment);
    void ReferenceAdded(void* slot) nothrow;
    void ReferenceRemoved(void* slot) nothrow;
    nuint Collect();
}
