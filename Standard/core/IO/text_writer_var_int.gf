using Standard::Binary;
namespace Standard::IO;

/// Sends the canonical shortest UInt32 base-128 representation in one counted Write.
/// Borrows the sink; failures may leave partial output. Does not flush or close it.
public void WriteVarUInt32(ITextWriter* output, uint value)
{
    byte[5] bytes;
    nuint written = 0;
    // The maximum-width stack buffer guarantees codec success.
    VarInt::TryWriteUInt32(bytes, 5, value, ref written);
    output.Write(((byte*)bytes).reinterpret<readonly(char)*>(), written);
}

extension ITextWriter
{
    public void WriteVarUInt32(uint value)
    => Standard::IO::WriteVarUInt32(this, value);
}

/// Sends the canonical shortest UInt64 base-128 representation in one counted Write.
/// Borrows the sink; failures may leave partial output. Does not flush or close it.
public void WriteVarUInt64(ITextWriter* output, ulong value)
{
    byte[10] bytes;
    nuint written = 0;
    VarInt::TryWriteUInt64(bytes, 10, value, ref written);
    output.Write(((byte*)bytes).reinterpret<readonly(char)*>(), written);
}

extension ITextWriter
{
    public void WriteVarUInt64(ulong value)
    => Standard::IO::WriteVarUInt64(this, value);
}

/// ZigZag-encodes Int32 before canonical base-128 output, including signed extrema.
public void WriteZigZagInt32(ITextWriter* output, int value)
    => WriteVarUInt32(output, VarInt::ZigZagEncode(value));

extension ITextWriter
{
    public void WriteZigZagInt32(int value)
    => Standard::IO::WriteZigZagInt32(this, value);
}

/// ZigZag-encodes Int64 before canonical base-128 output, including signed extrema.
public void WriteZigZagInt64(ITextWriter* output, long value)
    => WriteVarUInt64(output, VarInt::ZigZagEncode(value));

extension ITextWriter
{
    public void WriteZigZagInt64(long value)
    => Standard::IO::WriteZigZagInt64(this, value);
}
