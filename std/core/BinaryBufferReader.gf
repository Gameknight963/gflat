namespace std;

// Borrows counted bytes. Copies retain independent positions over the same storage.
public struct BinaryBufferReader
{
    private readonly(byte)*? data;
    private nuint length;
    private nuint position;

    public BinaryBufferReader() nothrow { data = null; length = 0; position = 0; }
    public BinaryBufferReader(readonly(byte)* storage, nuint count) nothrow
    {
        data = storage; length = count; position = 0;
    }

    public nuint Length { readonly get nothrow => length; }
    public readonly(byte)*? Data { readonly get nothrow => data; }
    public nuint Position { readonly get nothrow => position; }
    public nuint Remaining { readonly get nothrow => length - position; }
    public void Reset() nothrow { position = 0; }
    public bool TrySkip(nuint count) nothrow
    {
        if (count > length - position) return false;
        position += count; return true;
    }

    // Returns a bounded view of the next bytes. Output must not alias this reader.
    public bool TryReadSlice(nuint count, ref BinaryBufferReader slice) nothrow
    {
        if (count > length - position) { slice = new(); return false; }
        if (count == 0) slice = new();
        else slice = new(data! + position, count);
        position += count; return true;
    }

    public bool TryReadByte(ref byte value) nothrow
    {
        if (position == length) { value = (byte)0; return false; }
        byte result = data![position];
        value = result; position++; return true;
    }

    public bool TryReadInt16LittleEndian(ref short value) nothrow
    {
        if (length - position < 2) { value = (short)0; return false; }
        if (!BinaryPrimitives::TryReadInt16LittleEndian(data! + position, length - position, ref value)) return false;
        position += 2; return true;
    }

    public bool TryReadInt16BigEndian(ref short value) nothrow
    {
        if (length - position < 2) { value = (short)0; return false; }
        if (!BinaryPrimitives::TryReadInt16BigEndian(data! + position, length - position, ref value)) return false;
        position += 2; return true;
    }

    public bool TryReadUInt16LittleEndian(ref ushort value) nothrow
    {
        if (length - position < 2) { value = (ushort)0; return false; }
        if (!BinaryPrimitives::TryReadUInt16LittleEndian(data! + position, length - position, ref value)) return false;
        position += 2; return true;
    }

    public bool TryReadUInt16BigEndian(ref ushort value) nothrow
    {
        if (length - position < 2) { value = (ushort)0; return false; }
        if (!BinaryPrimitives::TryReadUInt16BigEndian(data! + position, length - position, ref value)) return false;
        position += 2; return true;
    }

    public bool TryReadInt32LittleEndian(ref int value) nothrow
    {
        if (length - position < 4) { value = (int)0; return false; }
        if (!BinaryPrimitives::TryReadInt32LittleEndian(data! + position, length - position, ref value)) return false;
        position += 4; return true;
    }

    public bool TryReadInt32BigEndian(ref int value) nothrow
    {
        if (length - position < 4) { value = (int)0; return false; }
        if (!BinaryPrimitives::TryReadInt32BigEndian(data! + position, length - position, ref value)) return false;
        position += 4; return true;
    }

    public bool TryReadUInt32LittleEndian(ref uint value) nothrow
    {
        if (length - position < 4) { value = (uint)0; return false; }
        if (!BinaryPrimitives::TryReadUInt32LittleEndian(data! + position, length - position, ref value)) return false;
        position += 4; return true;
    }

    public bool TryReadUInt32BigEndian(ref uint value) nothrow
    {
        if (length - position < 4) { value = (uint)0; return false; }
        if (!BinaryPrimitives::TryReadUInt32BigEndian(data! + position, length - position, ref value)) return false;
        position += 4; return true;
    }

    public bool TryReadInt64LittleEndian(ref long value) nothrow
    {
        if (length - position < 8) { value = (long)0; return false; }
        if (!BinaryPrimitives::TryReadInt64LittleEndian(data! + position, length - position, ref value)) return false;
        position += 8; return true;
    }

    public bool TryReadInt64BigEndian(ref long value) nothrow
    {
        if (length - position < 8) { value = (long)0; return false; }
        if (!BinaryPrimitives::TryReadInt64BigEndian(data! + position, length - position, ref value)) return false;
        position += 8; return true;
    }

    public bool TryReadUInt64LittleEndian(ref ulong value) nothrow
    {
        if (length - position < 8) { value = (ulong)0; return false; }
        if (!BinaryPrimitives::TryReadUInt64LittleEndian(data! + position, length - position, ref value)) return false;
        position += 8; return true;
    }

    public bool TryReadUInt64BigEndian(ref ulong value) nothrow
    {
        if (length - position < 8) { value = (ulong)0; return false; }
        if (!BinaryPrimitives::TryReadUInt64BigEndian(data! + position, length - position, ref value)) return false;
        position += 8; return true;
    }
}
