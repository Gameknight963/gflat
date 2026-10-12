// Default memory hooks. Replace both functions to supply a custom allocator.
extern void*? malloc(nuint size);
extern void free(void* ptr);

namespace Memory
{
    public weak void*? Allocate(nuint size)
    {
        return malloc(size);
    }

    public weak void Free(void* ptr)
    {
        free(ptr);
    }
}
