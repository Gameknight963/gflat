// Default allocation and deallocation functions. Each can be replaced independently.
namespace Memory
{
    // Keep native import names out of both global and replacement-hook lookup.
    namespace NativeImports
    {
        private extern "C" void*? malloc(nuint size);
        private extern "C" void free(void* ptr);
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

// Compiler runtime ABI adapted to the library's replaceable allocator functions.
namespace __gflat_runtime
{
    public void*? Allocate(nuint bytes) => global::Memory::Allocate(bytes);
    public void Free(void* storage) nothrow => global::Memory::Free(storage);
}
