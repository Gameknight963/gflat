using std::Binary;
namespace std::IO;

// Fixed-width scalar reads borrow the source and tolerate short positive reads.
// Early EOF or failure may consume a prefix; no rewind, close or rollback occurs.
// Exact-width stack buffers guarantee the corresponding codec succeeds.

/// Reads 2 bytes in little-endian order, preserving all representation bits.
public ushort ReadUInt16LittleEndian(this IByteReader* input)
{
    byte[2] buffer;
    ReadExactly(input, (char*)buffer, 2);
    ushort value = (ushort)0;
    BinaryPrimitives::TryReadUInt16LittleEndian(buffer, 2, ref value);
    return value;
}

/// Reads 2 bytes in big-endian order, preserving all representation bits.
public ushort ReadUInt16BigEndian(this IByteReader* input)
{
    byte[2] buffer;
    ReadExactly(input, (char*)buffer, 2);
    ushort value = (ushort)0;
    BinaryPrimitives::TryReadUInt16BigEndian(buffer, 2, ref value);
    return value;
}

/// Reads 4 bytes in little-endian order, preserving all representation bits.
public uint ReadUInt32LittleEndian(this IByteReader* input)
{
    byte[4] buffer;
    ReadExactly(input, (char*)buffer, 4);
    uint value = (uint)0;
    BinaryPrimitives::TryReadUInt32LittleEndian(buffer, 4, ref value);
    return value;
}

/// Reads 4 bytes in big-endian order, preserving all representation bits.
public uint ReadUInt32BigEndian(this IByteReader* input)
{
    byte[4] buffer;
    ReadExactly(input, (char*)buffer, 4);
    uint value = (uint)0;
    BinaryPrimitives::TryReadUInt32BigEndian(buffer, 4, ref value);
    return value;
}

/// Reads 8 bytes in little-endian order, preserving all representation bits.
public ulong ReadUInt64LittleEndian(this IByteReader* input)
{
    byte[8] buffer;
    ReadExactly(input, (char*)buffer, 8);
    ulong value = (ulong)0;
    BinaryPrimitives::TryReadUInt64LittleEndian(buffer, 8, ref value);
    return value;
}

/// Reads 8 bytes in big-endian order, preserving all representation bits.
public ulong ReadUInt64BigEndian(this IByteReader* input)
{
    byte[8] buffer;
    ReadExactly(input, (char*)buffer, 8);
    ulong value = (ulong)0;
    BinaryPrimitives::TryReadUInt64BigEndian(buffer, 8, ref value);
    return value;
}

/// Reads 2 bytes in little-endian order, preserving all representation bits.
public short ReadInt16LittleEndian(this IByteReader* input)
{
    byte[2] buffer;
    ReadExactly(input, (char*)buffer, 2);
    short value = (short)0;
    BinaryPrimitives::TryReadInt16LittleEndian(buffer, 2, ref value);
    return value;
}

/// Reads 2 bytes in big-endian order, preserving all representation bits.
public short ReadInt16BigEndian(this IByteReader* input)
{
    byte[2] buffer;
    ReadExactly(input, (char*)buffer, 2);
    short value = (short)0;
    BinaryPrimitives::TryReadInt16BigEndian(buffer, 2, ref value);
    return value;
}

/// Reads 4 bytes in little-endian order, preserving all representation bits.
public int ReadInt32LittleEndian(this IByteReader* input)
{
    byte[4] buffer;
    ReadExactly(input, (char*)buffer, 4);
    int value = (int)0;
    BinaryPrimitives::TryReadInt32LittleEndian(buffer, 4, ref value);
    return value;
}

/// Reads 4 bytes in big-endian order, preserving all representation bits.
public int ReadInt32BigEndian(this IByteReader* input)
{
    byte[4] buffer;
    ReadExactly(input, (char*)buffer, 4);
    int value = (int)0;
    BinaryPrimitives::TryReadInt32BigEndian(buffer, 4, ref value);
    return value;
}

/// Reads 8 bytes in little-endian order, preserving all representation bits.
public long ReadInt64LittleEndian(this IByteReader* input)
{
    byte[8] buffer;
    ReadExactly(input, (char*)buffer, 8);
    long value = (long)0;
    BinaryPrimitives::TryReadInt64LittleEndian(buffer, 8, ref value);
    return value;
}

/// Reads 8 bytes in big-endian order, preserving all representation bits.
public long ReadInt64BigEndian(this IByteReader* input)
{
    byte[8] buffer;
    ReadExactly(input, (char*)buffer, 8);
    long value = (long)0;
    BinaryPrimitives::TryReadInt64BigEndian(buffer, 8, ref value);
    return value;
}

/// Reads 4 bytes in little-endian order, preserving all representation bits.
public float ReadFloatLittleEndian(this IByteReader* input)
{
    byte[4] buffer;
    ReadExactly(input, (char*)buffer, 4);
    float value = (float)0;
    BinaryPrimitives::TryReadFloatLittleEndian(buffer, 4, ref value);
    return value;
}

/// Reads 4 bytes in big-endian order, preserving all representation bits.
public float ReadFloatBigEndian(this IByteReader* input)
{
    byte[4] buffer;
    ReadExactly(input, (char*)buffer, 4);
    float value = (float)0;
    BinaryPrimitives::TryReadFloatBigEndian(buffer, 4, ref value);
    return value;
}

/// Reads 8 bytes in little-endian order, preserving all representation bits.
public double ReadDoubleLittleEndian(this IByteReader* input)
{
    byte[8] buffer;
    ReadExactly(input, (char*)buffer, 8);
    double value = (double)0;
    BinaryPrimitives::TryReadDoubleLittleEndian(buffer, 8, ref value);
    return value;
}

/// Reads 8 bytes in big-endian order, preserving all representation bits.
public double ReadDoubleBigEndian(this IByteReader* input)
{
    byte[8] buffer;
    ReadExactly(input, (char*)buffer, 8);
    double value = (double)0;
    BinaryPrimitives::TryReadDoubleBigEndian(buffer, 8, ref value);
    return value;
}

/// Reads 16 bytes in little-endian order, preserving all representation bits.
public extralong ReadExtraLongLittleEndian(this IByteReader* input)
{
    byte[16] buffer;
    ReadExactly(input, (char*)buffer, 16);
    extralong value = (extralong)0;
    BinaryPrimitives::TryReadExtraLongLittleEndian(buffer, 16, ref value);
    return value;
}

/// Reads 16 bytes in big-endian order, preserving all representation bits.
public extralong ReadExtraLongBigEndian(this IByteReader* input)
{
    byte[16] buffer;
    ReadExactly(input, (char*)buffer, 16);
    extralong value = (extralong)0;
    BinaryPrimitives::TryReadExtraLongBigEndian(buffer, 16, ref value);
    return value;
}
