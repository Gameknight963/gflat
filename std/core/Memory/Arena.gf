namespace std::Memory;

// Owns bump-allocated raw storage. Reset invalidates every returned pointer;
// it never runs destructors for objects placed in that storage.
struct Arena
{
    private Block*? head;
    private nuint blockSize;

    private struct Block
    {
        public Block*? Next;
        public byte* Data;
        public nuint Capacity;
        public nuint Used;
        public Block(Block*? next, byte* data, nuint capacity)
        {
            Next = next; Data = data; Capacity = capacity; Used = 0;
        }
    }

    public Arena(nuint chunkSize = 4096)
    {
        if (chunkSize == 0) throw new Exception("Arena block size must be positive");
        head = null;
        blockSize = chunkSize;
    }

    public void* Allocate(nuint bytes, nuint alignment = (nuint)alignof(nuint))
    {
        if (alignment == 0 || (alignment & (alignment - 1)) != 0)
            throw new Exception("Arena alignment must be a positive power of two");
        if (bytes == 0) bytes = 1;
        nuint maximum = (nuint)0 - (nuint)1;
        nuint header = (nuint)sizeof(Block);
        nuint slack = alignment - 1;
        if (slack > maximum - header || bytes > maximum - header - slack)
            throw new Exception("Arena allocation size overflow");

        if (head != null)
        {
            Block* current = head!;
            nuint address = (current.Data).reinterpret<nuint>() + current.Used;
            nuint remainder = address & slack;
            nuint padding = 0;
            if (remainder != 0) padding = alignment - remainder;
            nuint remaining = current.Capacity - current.Used;
            if (padding <= remaining && bytes <= remaining - padding)
            {
                void* result = (void*)(current.Data + current.Used + padding);
                current.Used += padding + bytes;
                return result;
            }
        }

        nuint capacity = blockSize;
        if (bytes > capacity) capacity = bytes;
        if (capacity > maximum - header - slack) capacity = bytes;
        nuint payload = capacity + slack;
        void*? allocation = global::Memory::Allocate(header + payload);
        if (allocation == null) throw new BadAllocException("Arena allocation failed");
        byte* data = (byte*)allocation + header;
        Block* block = new* Block(head, data, payload) at (Block*)allocation!;
        nuint remainder = (data).reinterpret<nuint>() & slack;
        nuint padding = 0;
        if (remainder != 0) padding = alignment - remainder;
        block.Used = padding + bytes;
        head = block;
        return (void*)(data + padding);
    }

    public void* AllocateZeroed(nuint bytes, nuint alignment = (nuint)alignof(nuint))
    {
        if (bytes == 0) bytes = 1;
        void* result = this.Allocate(bytes, alignment);
        byte* output = (byte*)result;
        for (nuint i = 0; i < bytes; i++) output[i] = (byte)0;
        return result;
    }

    public void Reset() nothrow
    {
        while (head != null)
        {
            Block* current = head!;
            head = current.Next;
            global::Memory::Free((void*)current);
        }
    }

    ~Arena() { this.Reset(); }
}
