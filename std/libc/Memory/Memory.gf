// Default memory hooks. Replace both functions to supply a custom allocator.
namespace Memory
{
    // Keep native import names out of both global and replacement-hook lookup.
    namespace NativeImports
    {
        private extern void*? malloc(nuint size);
        private extern void free(void* ptr);
    }

    public weak void*? Allocate(nuint size)
    {
        return NativeImports::malloc(size);
    }

    public weak void Free(void* ptr) nothrow
    {
        NativeImports::free(ptr);
    }
}
