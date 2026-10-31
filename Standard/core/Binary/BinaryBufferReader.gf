using Standard::IO;
namespace Standard::Binary;

// Borrows counted bytes. Copies retain independent positions over the same storage.
public struct BinaryBufferReader : IByteReader
{
    private readonly(byte)*? data;
    private nuint length;
    private nuint position;

    public BinaryBufferReader() nothrow { data = null; length = 0; position = 0; }
    public BinaryBufferReader(readonly(byte)* storage, nuint count) nothrow
    {
        data = storage; length = count; position = 0;
    }

    public nuint Length { immutable get nothrow => length; }
    public readonly(byte)*? Data { immutable get nothrow => data; }
    public nuint Position { immutable get nothrow => position; }
    public nuint Remaining { immutable get nothrow => length - position; }
    public void Reset() nothrow { position = 0; }
    // Absolute position within the captured bytes; failure preserves the old position.
    public bool TrySeek(nuint newPosition) nothrow
    {
        if (newPosition > length) return false;
        position = newPosition; return true;
    }
    public bool TrySkip(nuint count) nothrow
    {
        if (count > length - position) return false;
        position += count; return true;
    }

    /// Consumes up to count bytes, returning a short prefix or zero at EOF.
    /// Zero requests and empty cursors do not dereference either storage pointer.
    /// Output must be disjoint from this reader and its entire captured input.
    public nuint Read(char* output, nuint count) nothrow
    {
        nuint take = length - position;
        if (take > count) take = count;
        if (take == 0) return 0;
        global::Memory::Move((void*)output, (readonly(void)*)(data! + position), take);
        position += take;
        return take;
    }
    // Overlap with the borrowed input bytes is permitted.
    public bool TryReadBytes(byte* output, nuint count) nothrow
    {
        if (count > length - position) return false;
        if (count == 0) return true;
        global::Memory::Move((void*)output, (readonly(void)*)(data! + position), count);
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

    /// Borrows the next count bytes without advancing this reader. The child starts at zero.
    /// Empty success and failure reset the child to empty. Keep output separate from this reader and backing bytes.
    public immutable bool TryPeekSlice(nuint count, ref BinaryBufferReader slice) nothrow
    {
        if (count > length - position) { slice = new(); return false; }
        if (count == 0) slice = new();
        else slice = new(data! + position, count);
        return true;
    }

    /// Borrows bytes before the first delimiter and consumes through that delimiter.
    /// Missing delimiter preserves Position and resets slice to default empty.
    /// Empty fields also return an empty child with null Data. No terminator is retained.
    /// Keep output disjoint from this reader and its entire captured input.
    public bool TryReadTo(byte delimiter, ref BinaryBufferReader slice) nothrow
    {
        slice = new();
        nuint remaining = length - position;
        for (nuint offset = 0; offset < remaining; offset++)
        {
            if (data![position + offset] != delimiter) continue;
            if (!this.TryReadSlice(offset, ref slice)) return false;
            position++;
            return true;
        }
        return false;
    }

    public immutable bool TryPeekByte(ref byte value) nothrow
    {
        if (position == length) { value = (byte)0; return false; }
        byte result = data![position];
        value = result; return true;
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

    public bool TryReadExtraLongLittleEndian(ref extralong value) nothrow
    {
        if (length - position < 16) { value = (extralong)0; return false; }
        if (!BinaryPrimitives::TryReadExtraLongLittleEndian(data! + position, length - position, ref value)) return false;
        position += 16; return true;
    }

    public bool TryReadExtraLongBigEndian(ref extralong value) nothrow
    {
        if (length - position < 16) { value = (extralong)0; return false; }
        if (!BinaryPrimitives::TryReadExtraLongBigEndian(data! + position, length - position, ref value)) return false;
        position += 16; return true;
    }

    public bool TryReadFloatLittleEndian(ref float value) nothrow
    {
        if (length - position < 4) { value = (float)0; return false; }
        if (!BinaryPrimitives::TryReadFloatLittleEndian(data! + position, length - position, ref value)) return false;
        position += 4; return true;
    }

    public bool TryReadFloatBigEndian(ref float value) nothrow
    {
        if (length - position < 4) { value = (float)0; return false; }
        if (!BinaryPrimitives::TryReadFloatBigEndian(data! + position, length - position, ref value)) return false;
        position += 4; return true;
    }

    public bool TryReadDoubleLittleEndian(ref double value) nothrow
    {
        if (length - position < 8) { value = (double)0; return false; }
        if (!BinaryPrimitives::TryReadDoubleLittleEndian(data! + position, length - position, ref value)) return false;
        position += 8; return true;
    }

    public bool TryReadDoubleBigEndian(ref double value) nothrow
    {
        if (length - position < 8) { value = (double)0; return false; }
        if (!BinaryPrimitives::TryReadDoubleBigEndian(data! + position, length - position, ref value)) return false;
        position += 8; return true;
    }
}
