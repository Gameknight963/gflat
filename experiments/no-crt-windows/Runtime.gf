// Experimental Windows x64 runtime. These declarations use Windows ABI types:
// BOOL is int (not gflat bool), DWORD is uint, SIZE_T is nuint.
private extern void* GetProcessHeap();
private extern void*? HeapAlloc(void* heap, uint flags, nuint bytes);
private extern int HeapFree(void* heap, uint flags, void* storage);
private extern void*? GetStdHandle(uint identifier);
private extern int WriteFile(void* file, readonly(void)* data, uint count, uint* written, void*? overlapped);
private extern void ExitProcess(uint code);

namespace Memory
{
    public void*? Allocate(nuint bytes) => HeapAlloc(GetProcessHeap(), 0, bytes);

    public void Free(void* storage)
    {
        if (HeapFree(GetProcessHeap(), 0, storage) == 0) ExitProcess(11);
    }
}

namespace Platform
{
    // Counted output deliberately avoids depending on a C strlen implementation.
    public bool Write(readonly(char*) text, uint count)
    {
        void*? output = GetStdHandle((uint)-11);
        if (output == null || output == (void*)-1) return false;
        uint offset = 0;
        while (offset < count)
        {
            uint written = 0;
            if (WriteFile(output!, (readonly(void)*)(text + offset), count - offset, &written, null) == 0)
                return false;
            if (written == (uint)0) return false;
            offset += written;
        }
        return true;
    }

    public void Exit(uint code) { ExitProcess(code); }
}
