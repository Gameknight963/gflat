namespace std::Binary;

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

    public nuint Capacity { immutable get nothrow => capacity; }
    public byte*? Data { get nothrow => data; }
    public nuint Position { immutable get nothrow => position; }
    public nuint Remaining { immutable get nothrow => capacity - position; }
    public void Reset() nothrow { position = 0; }
    // Absolute position within the captured bytes; failure preserves the old position.
    public bool TrySeek(nuint newPosition) nothrow
    {
        if (newPosition > capacity) return false;
        position = newPosition; return true;
    }
    public bool TrySkip(nuint count) nothrow
    {
        if (count > capacity - position) return false;
        position += count; return true;
    }

    // Overlap with the borrowed output bytes is permitted.
    public bool TryWriteBytes(readonly(byte)* input, nuint count) nothrow
    {
        if (count > capacity - position) return false;
        if (count == 0) return true;
        global::Memory::Move((void*)(data! + position), (readonly(void)*)input, count);
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

    public bool TryWriteExtraLongLittleEndian(extralong value) nothrow
    {
        if (capacity - position < 16) return false;
        if (!BinaryPrimitives::TryWriteExtraLongLittleEndian(data! + position, capacity - position, value)) return false;
        position += 16; return true;
    }

    public bool TryWriteExtraLongBigEndian(extralong value) nothrow
    {
        if (capacity - position < 16) return false;
        if (!BinaryPrimitives::TryWriteExtraLongBigEndian(data! + position, capacity - position, value)) return false;
        position += 16; return true;
    }

    public bool TryWriteFloatLittleEndian(float value) nothrow
    {
        if (capacity - position < 4) return false;
        if (!BinaryPrimitives::TryWriteFloatLittleEndian(data! + position, capacity - position, value)) return false;
        position += 4; return true;
    }

    public bool TryWriteFloatBigEndian(float value) nothrow
    {
        if (capacity - position < 4) return false;
        if (!BinaryPrimitives::TryWriteFloatBigEndian(data! + position, capacity - position, value)) return false;
        position += 4; return true;
    }

    public bool TryWriteDoubleLittleEndian(double value) nothrow
    {
        if (capacity - position < 8) return false;
        if (!BinaryPrimitives::TryWriteDoubleLittleEndian(data! + position, capacity - position, value)) return false;
        position += 8; return true;
    }

    public bool TryWriteDoubleBigEndian(double value) nothrow
    {
        if (capacity - position < 8) return false;
        if (!BinaryPrimitives::TryWriteDoubleBigEndian(data! + position, capacity - position, value)) return false;
        position += 8; return true;
    }
}
