using std::Binary;
namespace std::IO;

// Fixed-width scalar reads borrow the source and tolerate short positive reads.
// Early EOF or failure may consume a prefix; no rewind, close or rollback occurs.
// Exact-width stack buffers guarantee the corresponding codec succeeds.

/// Reads 2 bytes in little-endian order, preserving all representation bits.
public ushort ReadUInt16LittleEndian(IByteReader* input)
{
    byte[2] buffer;
    ReadExactly(input, ((byte*)buffer).reinterpret<char*>(), 2);
    ushort value = (ushort)0;
    BinaryPrimitives::TryReadUInt16LittleEndian(buffer, 2, ref value);
    return value;
}

extension IByteReader
{
    public ushort ReadUInt16LittleEndian()
    => std::IO::ReadUInt16LittleEndian(this);
}

/// Reads 2 bytes in big-endian order, preserving all representation bits.
public ushort ReadUInt16BigEndian(IByteReader* input)
{
    byte[2] buffer;
    ReadExactly(input, ((byte*)buffer).reinterpret<char*>(), 2);
    ushort value = (ushort)0;
    BinaryPrimitives::TryReadUInt16BigEndian(buffer, 2, ref value);
    return value;
}

extension IByteReader
{
    public ushort ReadUInt16BigEndian()
    => std::IO::ReadUInt16BigEndian(this);
}

/// Reads 4 bytes in little-endian order, preserving all representation bits.
public uint ReadUInt32LittleEndian(IByteReader* input)
{
    byte[4] buffer;
    ReadExactly(input, ((byte*)buffer).reinterpret<char*>(), 4);
    uint value = (uint)0;
    BinaryPrimitives::TryReadUInt32LittleEndian(buffer, 4, ref value);
    return value;
}

extension IByteReader
{
    public uint ReadUInt32LittleEndian()
    => std::IO::ReadUInt32LittleEndian(this);
}

/// Reads 4 bytes in big-endian order, preserving all representation bits.
public uint ReadUInt32BigEndian(IByteReader* input)
{
    byte[4] buffer;
    ReadExactly(input, ((byte*)buffer).reinterpret<char*>(), 4);
    uint value = (uint)0;
    BinaryPrimitives::TryReadUInt32BigEndian(buffer, 4, ref value);
    return value;
}

extension IByteReader
{
    public uint ReadUInt32BigEndian()
    => std::IO::ReadUInt32BigEndian(this);
}

/// Reads 8 bytes in little-endian order, preserving all representation bits.
public ulong ReadUInt64LittleEndian(IByteReader* input)
{
    byte[8] buffer;
    ReadExactly(input, ((byte*)buffer).reinterpret<char*>(), 8);
    ulong value = (ulong)0;
    BinaryPrimitives::TryReadUInt64LittleEndian(buffer, 8, ref value);
    return value;
}

extension IByteReader
{
    public ulong ReadUInt64LittleEndian()
    => std::IO::ReadUInt64LittleEndian(this);
}

/// Reads 8 bytes in big-endian order, preserving all representation bits.
public ulong ReadUInt64BigEndian(IByteReader* input)
{
    byte[8] buffer;
    ReadExactly(input, ((byte*)buffer).reinterpret<char*>(), 8);
    ulong value = (ulong)0;
    BinaryPrimitives::TryReadUInt64BigEndian(buffer, 8, ref value);
    return value;
}

extension IByteReader
{
    public ulong ReadUInt64BigEndian()
    => std::IO::ReadUInt64BigEndian(this);
}

/// Reads 2 bytes in little-endian order, preserving all representation bits.
public short ReadInt16LittleEndian(IByteReader* input)
{
    byte[2] buffer;
    ReadExactly(input, ((byte*)buffer).reinterpret<char*>(), 2);
    short value = (short)0;
    BinaryPrimitives::TryReadInt16LittleEndian(buffer, 2, ref value);
    return value;
}

extension IByteReader
{
    public short ReadInt16LittleEndian()
    => std::IO::ReadInt16LittleEndian(this);
}

/// Reads 2 bytes in big-endian order, preserving all representation bits.
public short ReadInt16BigEndian(IByteReader* input)
{
    byte[2] buffer;
    ReadExactly(input, ((byte*)buffer).reinterpret<char*>(), 2);
    short value = (short)0;
    BinaryPrimitives::TryReadInt16BigEndian(buffer, 2, ref value);
    return value;
}

extension IByteReader
{
    public short ReadInt16BigEndian()
    => std::IO::ReadInt16BigEndian(this);
}

/// Reads 4 bytes in little-endian order, preserving all representation bits.
public int ReadInt32LittleEndian(IByteReader* input)
{
    byte[4] buffer;
    ReadExactly(input, ((byte*)buffer).reinterpret<char*>(), 4);
    int value = (int)0;
    BinaryPrimitives::TryReadInt32LittleEndian(buffer, 4, ref value);
    return value;
}

extension IByteReader
{
    public int ReadInt32LittleEndian()
    => std::IO::ReadInt32LittleEndian(this);
}

/// Reads 4 bytes in big-endian order, preserving all representation bits.
public int ReadInt32BigEndian(IByteReader* input)
{
    byte[4] buffer;
    ReadExactly(input, ((byte*)buffer).reinterpret<char*>(), 4);
    int value = (int)0;
    BinaryPrimitives::TryReadInt32BigEndian(buffer, 4, ref value);
    return value;
}

extension IByteReader
{
    public int ReadInt32BigEndian()
    => std::IO::ReadInt32BigEndian(this);
}

/// Reads 8 bytes in little-endian order, preserving all representation bits.
public long ReadInt64LittleEndian(IByteReader* input)
{
    byte[8] buffer;
    ReadExactly(input, ((byte*)buffer).reinterpret<char*>(), 8);
    long value = (long)0;
    BinaryPrimitives::TryReadInt64LittleEndian(buffer, 8, ref value);
    return value;
}

extension IByteReader
{
    public long ReadInt64LittleEndian()
    => std::IO::ReadInt64LittleEndian(this);
}

/// Reads 8 bytes in big-endian order, preserving all representation bits.
public long ReadInt64BigEndian(IByteReader* input)
{
    byte[8] buffer;
    ReadExactly(input, ((byte*)buffer).reinterpret<char*>(), 8);
    long value = (long)0;
    BinaryPrimitives::TryReadInt64BigEndian(buffer, 8, ref value);
    return value;
}

extension IByteReader
{
    public long ReadInt64BigEndian()
    => std::IO::ReadInt64BigEndian(this);
}

/// Reads 4 bytes in little-endian order, preserving all representation bits.
public float ReadFloatLittleEndian(IByteReader* input)
{
    byte[4] buffer;
    ReadExactly(input, ((byte*)buffer).reinterpret<char*>(), 4);
    float value = (float)0;
    BinaryPrimitives::TryReadFloatLittleEndian(buffer, 4, ref value);
    return value;
}

extension IByteReader
{
    public float ReadFloatLittleEndian()
    => std::IO::ReadFloatLittleEndian(this);
}

/// Reads 4 bytes in big-endian order, preserving all representation bits.
public float ReadFloatBigEndian(IByteReader* input)
{
    byte[4] buffer;
    ReadExactly(input, ((byte*)buffer).reinterpret<char*>(), 4);
    float value = (float)0;
    BinaryPrimitives::TryReadFloatBigEndian(buffer, 4, ref value);
    return value;
}

extension IByteReader
{
    public float ReadFloatBigEndian()
    => std::IO::ReadFloatBigEndian(this);
}

/// Reads 8 bytes in little-endian order, preserving all representation bits.
public double ReadDoubleLittleEndian(IByteReader* input)
{
    byte[8] buffer;
    ReadExactly(input, ((byte*)buffer).reinterpret<char*>(), 8);
    double value = (double)0;
    BinaryPrimitives::TryReadDoubleLittleEndian(buffer, 8, ref value);
    return value;
}

extension IByteReader
{
    public double ReadDoubleLittleEndian()
    => std::IO::ReadDoubleLittleEndian(this);
}

/// Reads 8 bytes in big-endian order, preserving all representation bits.
public double ReadDoubleBigEndian(IByteReader* input)
{
    byte[8] buffer;
    ReadExactly(input, ((byte*)buffer).reinterpret<char*>(), 8);
    double value = (double)0;
    BinaryPrimitives::TryReadDoubleBigEndian(buffer, 8, ref value);
    return value;
}

extension IByteReader
{
    public double ReadDoubleBigEndian()
    => std::IO::ReadDoubleBigEndian(this);
}

/// Reads 16 bytes in little-endian order, preserving all representation bits.
public extralong ReadExtraLongLittleEndian(IByteReader* input)
{
    byte[16] buffer;
    ReadExactly(input, ((byte*)buffer).reinterpret<char*>(), 16);
    extralong value = (extralong)0;
    BinaryPrimitives::TryReadExtraLongLittleEndian(buffer, 16, ref value);
    return value;
}

extension IByteReader
{
    public extralong ReadExtraLongLittleEndian()
    => std::IO::ReadExtraLongLittleEndian(this);
}

/// Reads 16 bytes in big-endian order, preserving all representation bits.
public extralong ReadExtraLongBigEndian(IByteReader* input)
{
    byte[16] buffer;
    ReadExactly(input, ((byte*)buffer).reinterpret<char*>(), 16);
    extralong value = (extralong)0;
    BinaryPrimitives::TryReadExtraLongBigEndian(buffer, 16, ref value);
    return value;
}

extension IByteReader
{
    public extralong ReadExtraLongBigEndian()
    => std::IO::ReadExtraLongBigEndian(this);
}
