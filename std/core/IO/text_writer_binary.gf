using std::Binary;
namespace std::IO;

// Fixed-width scalar writes borrow a synchronous counted sink.
// Each value is sent in one Write call; failure may leave a written prefix.
// No flush, close or rollback occurs. Exact-width buffers guarantee codec success.

/// Writes 2 bytes in little-endian order, preserving all representation bits.
public void WriteUInt16LittleEndian(this ITextWriter* output, ushort value)
{
    byte[2] buffer;
    BinaryPrimitives::TryWriteUInt16LittleEndian(buffer, 2, value);
    output.Write((readonly(char)*)buffer, 2);
}

/// Writes 2 bytes in big-endian order, preserving all representation bits.
public void WriteUInt16BigEndian(this ITextWriter* output, ushort value)
{
    byte[2] buffer;
    BinaryPrimitives::TryWriteUInt16BigEndian(buffer, 2, value);
    output.Write((readonly(char)*)buffer, 2);
}

/// Writes 4 bytes in little-endian order, preserving all representation bits.
public void WriteUInt32LittleEndian(this ITextWriter* output, uint value)
{
    byte[4] buffer;
    BinaryPrimitives::TryWriteUInt32LittleEndian(buffer, 4, value);
    output.Write((readonly(char)*)buffer, 4);
}

/// Writes 4 bytes in big-endian order, preserving all representation bits.
public void WriteUInt32BigEndian(this ITextWriter* output, uint value)
{
    byte[4] buffer;
    BinaryPrimitives::TryWriteUInt32BigEndian(buffer, 4, value);
    output.Write((readonly(char)*)buffer, 4);
}

/// Writes 8 bytes in little-endian order, preserving all representation bits.
public void WriteUInt64LittleEndian(this ITextWriter* output, ulong value)
{
    byte[8] buffer;
    BinaryPrimitives::TryWriteUInt64LittleEndian(buffer, 8, value);
    output.Write((readonly(char)*)buffer, 8);
}

/// Writes 8 bytes in big-endian order, preserving all representation bits.
public void WriteUInt64BigEndian(this ITextWriter* output, ulong value)
{
    byte[8] buffer;
    BinaryPrimitives::TryWriteUInt64BigEndian(buffer, 8, value);
    output.Write((readonly(char)*)buffer, 8);
}

/// Writes 2 bytes in little-endian order, preserving all representation bits.
public void WriteInt16LittleEndian(this ITextWriter* output, short value)
{
    byte[2] buffer;
    BinaryPrimitives::TryWriteInt16LittleEndian(buffer, 2, value);
    output.Write((readonly(char)*)buffer, 2);
}

/// Writes 2 bytes in big-endian order, preserving all representation bits.
public void WriteInt16BigEndian(this ITextWriter* output, short value)
{
    byte[2] buffer;
    BinaryPrimitives::TryWriteInt16BigEndian(buffer, 2, value);
    output.Write((readonly(char)*)buffer, 2);
}

/// Writes 4 bytes in little-endian order, preserving all representation bits.
public void WriteInt32LittleEndian(this ITextWriter* output, int value)
{
    byte[4] buffer;
    BinaryPrimitives::TryWriteInt32LittleEndian(buffer, 4, value);
    output.Write((readonly(char)*)buffer, 4);
}

/// Writes 4 bytes in big-endian order, preserving all representation bits.
public void WriteInt32BigEndian(this ITextWriter* output, int value)
{
    byte[4] buffer;
    BinaryPrimitives::TryWriteInt32BigEndian(buffer, 4, value);
    output.Write((readonly(char)*)buffer, 4);
}

/// Writes 8 bytes in little-endian order, preserving all representation bits.
public void WriteInt64LittleEndian(this ITextWriter* output, long value)
{
    byte[8] buffer;
    BinaryPrimitives::TryWriteInt64LittleEndian(buffer, 8, value);
    output.Write((readonly(char)*)buffer, 8);
}

/// Writes 8 bytes in big-endian order, preserving all representation bits.
public void WriteInt64BigEndian(this ITextWriter* output, long value)
{
    byte[8] buffer;
    BinaryPrimitives::TryWriteInt64BigEndian(buffer, 8, value);
    output.Write((readonly(char)*)buffer, 8);
}

/// Writes 4 bytes in little-endian order, preserving all representation bits.
public void WriteFloatLittleEndian(this ITextWriter* output, float value)
{
    byte[4] buffer;
    BinaryPrimitives::TryWriteFloatLittleEndian(buffer, 4, value);
    output.Write((readonly(char)*)buffer, 4);
}

/// Writes 4 bytes in big-endian order, preserving all representation bits.
public void WriteFloatBigEndian(this ITextWriter* output, float value)
{
    byte[4] buffer;
    BinaryPrimitives::TryWriteFloatBigEndian(buffer, 4, value);
    output.Write((readonly(char)*)buffer, 4);
}

/// Writes 8 bytes in little-endian order, preserving all representation bits.
public void WriteDoubleLittleEndian(this ITextWriter* output, double value)
{
    byte[8] buffer;
    BinaryPrimitives::TryWriteDoubleLittleEndian(buffer, 8, value);
    output.Write((readonly(char)*)buffer, 8);
}

/// Writes 8 bytes in big-endian order, preserving all representation bits.
public void WriteDoubleBigEndian(this ITextWriter* output, double value)
{
    byte[8] buffer;
    BinaryPrimitives::TryWriteDoubleBigEndian(buffer, 8, value);
    output.Write((readonly(char)*)buffer, 8);
}

/// Writes 16 bytes in little-endian order, preserving all representation bits.
public void WriteExtraLongLittleEndian(this ITextWriter* output, extralong value)
{
    byte[16] buffer;
    BinaryPrimitives::TryWriteExtraLongLittleEndian(buffer, 16, value);
    output.Write((readonly(char)*)buffer, 16);
}

/// Writes 16 bytes in big-endian order, preserving all representation bits.
public void WriteExtraLongBigEndian(this ITextWriter* output, extralong value)
{
    byte[16] buffer;
    BinaryPrimitives::TryWriteExtraLongBigEndian(buffer, 16, value);
    output.Write((readonly(char)*)buffer, 16);
}
