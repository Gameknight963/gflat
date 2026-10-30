using Standard::Binary;
namespace Standard::IO;

// Fixed-width scalar writes borrow a synchronous counted sink.
// Each value is sent in one Write call; failure may leave a written prefix.
// No flush, close or rollback occurs. Exact-width buffers guarantee codec success.

/// Writes 2 bytes in little-endian order, preserving all representation bits.
public void WriteUInt16LittleEndian(ITextWriter* output, ushort value)
{
    byte[2] buffer;
    BinaryPrimitives::TryWriteUInt16LittleEndian(buffer, 2, value);
    output.Write(((byte*)buffer).reinterpret<readonly(char)*>(), 2);
}

extension ITextWriter
{
    public void WriteUInt16LittleEndian(ushort value)
    => Standard::IO::WriteUInt16LittleEndian(this, value);
}

/// Writes 2 bytes in big-endian order, preserving all representation bits.
public void WriteUInt16BigEndian(ITextWriter* output, ushort value)
{
    byte[2] buffer;
    BinaryPrimitives::TryWriteUInt16BigEndian(buffer, 2, value);
    output.Write(((byte*)buffer).reinterpret<readonly(char)*>(), 2);
}

extension ITextWriter
{
    public void WriteUInt16BigEndian(ushort value)
    => Standard::IO::WriteUInt16BigEndian(this, value);
}

/// Writes 4 bytes in little-endian order, preserving all representation bits.
public void WriteUInt32LittleEndian(ITextWriter* output, uint value)
{
    byte[4] buffer;
    BinaryPrimitives::TryWriteUInt32LittleEndian(buffer, 4, value);
    output.Write(((byte*)buffer).reinterpret<readonly(char)*>(), 4);
}

extension ITextWriter
{
    public void WriteUInt32LittleEndian(uint value)
    => Standard::IO::WriteUInt32LittleEndian(this, value);
}

/// Writes 4 bytes in big-endian order, preserving all representation bits.
public void WriteUInt32BigEndian(ITextWriter* output, uint value)
{
    byte[4] buffer;
    BinaryPrimitives::TryWriteUInt32BigEndian(buffer, 4, value);
    output.Write(((byte*)buffer).reinterpret<readonly(char)*>(), 4);
}

extension ITextWriter
{
    public void WriteUInt32BigEndian(uint value)
    => Standard::IO::WriteUInt32BigEndian(this, value);
}

/// Writes 8 bytes in little-endian order, preserving all representation bits.
public void WriteUInt64LittleEndian(ITextWriter* output, ulong value)
{
    byte[8] buffer;
    BinaryPrimitives::TryWriteUInt64LittleEndian(buffer, 8, value);
    output.Write(((byte*)buffer).reinterpret<readonly(char)*>(), 8);
}

extension ITextWriter
{
    public void WriteUInt64LittleEndian(ulong value)
    => Standard::IO::WriteUInt64LittleEndian(this, value);
}

/// Writes 8 bytes in big-endian order, preserving all representation bits.
public void WriteUInt64BigEndian(ITextWriter* output, ulong value)
{
    byte[8] buffer;
    BinaryPrimitives::TryWriteUInt64BigEndian(buffer, 8, value);
    output.Write(((byte*)buffer).reinterpret<readonly(char)*>(), 8);
}

extension ITextWriter
{
    public void WriteUInt64BigEndian(ulong value)
    => Standard::IO::WriteUInt64BigEndian(this, value);
}

/// Writes 2 bytes in little-endian order, preserving all representation bits.
public void WriteInt16LittleEndian(ITextWriter* output, short value)
{
    byte[2] buffer;
    BinaryPrimitives::TryWriteInt16LittleEndian(buffer, 2, value);
    output.Write(((byte*)buffer).reinterpret<readonly(char)*>(), 2);
}

extension ITextWriter
{
    public void WriteInt16LittleEndian(short value)
    => Standard::IO::WriteInt16LittleEndian(this, value);
}

/// Writes 2 bytes in big-endian order, preserving all representation bits.
public void WriteInt16BigEndian(ITextWriter* output, short value)
{
    byte[2] buffer;
    BinaryPrimitives::TryWriteInt16BigEndian(buffer, 2, value);
    output.Write(((byte*)buffer).reinterpret<readonly(char)*>(), 2);
}

extension ITextWriter
{
    public void WriteInt16BigEndian(short value)
    => Standard::IO::WriteInt16BigEndian(this, value);
}

/// Writes 4 bytes in little-endian order, preserving all representation bits.
public void WriteInt32LittleEndian(ITextWriter* output, int value)
{
    byte[4] buffer;
    BinaryPrimitives::TryWriteInt32LittleEndian(buffer, 4, value);
    output.Write(((byte*)buffer).reinterpret<readonly(char)*>(), 4);
}

extension ITextWriter
{
    public void WriteInt32LittleEndian(int value)
    => Standard::IO::WriteInt32LittleEndian(this, value);
}

/// Writes 4 bytes in big-endian order, preserving all representation bits.
public void WriteInt32BigEndian(ITextWriter* output, int value)
{
    byte[4] buffer;
    BinaryPrimitives::TryWriteInt32BigEndian(buffer, 4, value);
    output.Write(((byte*)buffer).reinterpret<readonly(char)*>(), 4);
}

extension ITextWriter
{
    public void WriteInt32BigEndian(int value)
    => Standard::IO::WriteInt32BigEndian(this, value);
}

/// Writes 8 bytes in little-endian order, preserving all representation bits.
public void WriteInt64LittleEndian(ITextWriter* output, long value)
{
    byte[8] buffer;
    BinaryPrimitives::TryWriteInt64LittleEndian(buffer, 8, value);
    output.Write(((byte*)buffer).reinterpret<readonly(char)*>(), 8);
}

extension ITextWriter
{
    public void WriteInt64LittleEndian(long value)
    => Standard::IO::WriteInt64LittleEndian(this, value);
}

/// Writes 8 bytes in big-endian order, preserving all representation bits.
public void WriteInt64BigEndian(ITextWriter* output, long value)
{
    byte[8] buffer;
    BinaryPrimitives::TryWriteInt64BigEndian(buffer, 8, value);
    output.Write(((byte*)buffer).reinterpret<readonly(char)*>(), 8);
}

extension ITextWriter
{
    public void WriteInt64BigEndian(long value)
    => Standard::IO::WriteInt64BigEndian(this, value);
}

/// Writes 4 bytes in little-endian order, preserving all representation bits.
public void WriteFloatLittleEndian(ITextWriter* output, float value)
{
    byte[4] buffer;
    BinaryPrimitives::TryWriteFloatLittleEndian(buffer, 4, value);
    output.Write(((byte*)buffer).reinterpret<readonly(char)*>(), 4);
}

extension ITextWriter
{
    public void WriteFloatLittleEndian(float value)
    => Standard::IO::WriteFloatLittleEndian(this, value);
}

/// Writes 4 bytes in big-endian order, preserving all representation bits.
public void WriteFloatBigEndian(ITextWriter* output, float value)
{
    byte[4] buffer;
    BinaryPrimitives::TryWriteFloatBigEndian(buffer, 4, value);
    output.Write(((byte*)buffer).reinterpret<readonly(char)*>(), 4);
}

extension ITextWriter
{
    public void WriteFloatBigEndian(float value)
    => Standard::IO::WriteFloatBigEndian(this, value);
}

/// Writes 8 bytes in little-endian order, preserving all representation bits.
public void WriteDoubleLittleEndian(ITextWriter* output, double value)
{
    byte[8] buffer;
    BinaryPrimitives::TryWriteDoubleLittleEndian(buffer, 8, value);
    output.Write(((byte*)buffer).reinterpret<readonly(char)*>(), 8);
}

extension ITextWriter
{
    public void WriteDoubleLittleEndian(double value)
    => Standard::IO::WriteDoubleLittleEndian(this, value);
}

/// Writes 8 bytes in big-endian order, preserving all representation bits.
public void WriteDoubleBigEndian(ITextWriter* output, double value)
{
    byte[8] buffer;
    BinaryPrimitives::TryWriteDoubleBigEndian(buffer, 8, value);
    output.Write(((byte*)buffer).reinterpret<readonly(char)*>(), 8);
}

extension ITextWriter
{
    public void WriteDoubleBigEndian(double value)
    => Standard::IO::WriteDoubleBigEndian(this, value);
}

/// Writes 16 bytes in little-endian order, preserving all representation bits.
public void WriteExtraLongLittleEndian(ITextWriter* output, extralong value)
{
    byte[16] buffer;
    BinaryPrimitives::TryWriteExtraLongLittleEndian(buffer, 16, value);
    output.Write(((byte*)buffer).reinterpret<readonly(char)*>(), 16);
}

extension ITextWriter
{
    public void WriteExtraLongLittleEndian(extralong value)
    => Standard::IO::WriteExtraLongLittleEndian(this, value);
}

/// Writes 16 bytes in big-endian order, preserving all representation bits.
public void WriteExtraLongBigEndian(ITextWriter* output, extralong value)
{
    byte[16] buffer;
    BinaryPrimitives::TryWriteExtraLongBigEndian(buffer, 16, value);
    output.Write(((byte*)buffer).reinterpret<readonly(char)*>(), 16);
}

extension ITextWriter
{
    public void WriteExtraLongBigEndian(extralong value)
    => Standard::IO::WriteExtraLongBigEndian(this, value);
}
