namespace std;

// Represents the core contract a garbage collector must implement.
// Managed references require the first three operations. Collect is a common
// library operation; the compiler never inserts collection requests itself.
public interface IGarbageCollector
{
    static void* Allocate(nuint bytes, nuint alignment);
    static void ReferenceAdded(void* slot) nothrow;
    static void ReferenceRemoved(void* slot) nothrow;
    static nuint Collect();
}
