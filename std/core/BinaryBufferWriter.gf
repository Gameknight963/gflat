namespace std;

// Borrows writable bytes. Reset changes position, without clearing caller storage.
public struct BinaryBufferWriter
{
    private byte*? data;
    private nuint capacity;
    private nuint position;

    public BinaryBufferWriter() nothrow { data = null; capacity = 0; position = 0; }
    public BinaryBufferWriter(byte* storage, nuint count) nothrow
    {
        data = storage; capacity = count; position = 0;
    }

    public nuint Capacity { readonly get nothrow => capacity; }
    public nuint Position { readonly get nothrow => position; }
    public nuint Remaining { readonly get nothrow => capacity - position; }
    public void Reset() nothrow { position = 0; }
    public bool TrySkip(nuint count) nothrow
    {
        if (count > capacity - position) return false;
        position += count; return true;
    }

    // Reserves a bounded writable view, without initializing its bytes.
    // Output must not alias this writer.
    public bool TryWriteSlice(nuint count, ref BinaryBufferWriter slice) nothrow
    {
        if (count > capacity - position) { slice = new(); return false; }
        if (count == 0) slice = new();
        else slice = new(data! + position, count);
        position += count; return true;
    }

    public bool TryWriteByte(byte value) nothrow
    {
        if (position == capacity) return false;
        data![position] = value; position++; return true;
    }

    public bool TryWriteInt16LittleEndian(short value) nothrow
    {
        if (capacity - position < 2) return false;
        if (!BinaryPrimitives::TryWriteInt16LittleEndian(data! + position, capacity - position, value)) return false;
        position += 2; return true;
    }

    public bool TryWriteInt16BigEndian(short value) nothrow
    {
        if (capacity - position < 2) return false;
        if (!BinaryPrimitives::TryWriteInt16BigEndian(data! + position, capacity - position, value)) return false;
        position += 2; return true;
    }

    public bool TryWriteUInt16LittleEndian(ushort value) nothrow
    {
        if (capacity - position < 2) return false;
        if (!BinaryPrimitives::TryWriteUInt16LittleEndian(data! + position, capacity - position, value)) return false;
        position += 2; return true;
    }

    public bool TryWriteUInt16BigEndian(ushort value) nothrow
    {
        if (capacity - position < 2) return false;
        if (!BinaryPrimitives::TryWriteUInt16BigEndian(data! + position, capacity - position, value)) return false;
        position += 2; return true;
    }

    public bool TryWriteInt32LittleEndian(int value) nothrow
    {
        if (capacity - position < 4) return false;
        if (!BinaryPrimitives::TryWriteInt32LittleEndian(data! + position, capacity - position, value)) return false;
        position += 4; return true;
    }

    public bool TryWriteInt32BigEndian(int value) nothrow
    {
        if (capacity - position < 4) return false;
        if (!BinaryPrimitives::TryWriteInt32BigEndian(data! + position, capacity - position, value)) return false;
        position += 4; return true;
    }

    public bool TryWriteUInt32LittleEndian(uint value) nothrow
    {
        if (capacity - position < 4) return false;
        if (!BinaryPrimitives::TryWriteUInt32LittleEndian(data! + position, capacity - position, value)) return false;
        position += 4; return true;
    }

    public bool TryWriteUInt32BigEndian(uint value) nothrow
    {
        if (capacity - position < 4) return false;
        if (!BinaryPrimitives::TryWriteUInt32BigEndian(data! + position, capacity - position, value)) return false;
        position += 4; return true;
    }

    public bool TryWriteInt64LittleEndian(long value) nothrow
    {
        if (capacity - position < 8) return false;
        if (!BinaryPrimitives::TryWriteInt64LittleEndian(data! + position, capacity - position, value)) return false;
        position += 8; return true;
    }

    public bool TryWriteInt64BigEndian(long value) nothrow
    {
        if (capacity - position < 8) return false;
        if (!BinaryPrimitives::TryWriteInt64BigEndian(data! + position, capacity - position, value)) return false;
        position += 8; return true;
    }

    public bool TryWriteUInt64LittleEndian(ulong value) nothrow
    {
        if (capacity - position < 8) return false;
        if (!BinaryPrimitives::TryWriteUInt64LittleEndian(data! + position, capacity - position, value)) return false;
        position += 8; return true;
    }

    public bool TryWriteUInt64BigEndian(ulong value) nothrow
    {
        if (capacity - position < 8) return false;
        if (!BinaryPrimitives::TryWriteUInt64BigEndian(data! + position, capacity - position, value)) return false;
        position += 8; return true;
    }
}
