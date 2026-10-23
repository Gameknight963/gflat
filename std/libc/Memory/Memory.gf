// Default memory hooks. Replace both functions to supply a custom allocator.
private extern void*? malloc(nuint size);
private extern void free(void* ptr);

namespace Memory
{
    public weak void*? Allocate(nuint size)
    {
        return malloc(size);
    }

    public weak void Free(void* ptr) nothrow
    {
        free(ptr);
    }
}
