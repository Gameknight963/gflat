using std::Binary;
namespace std::IO;

/// Sends the canonical shortest UInt32 base-128 representation in one counted Write.
/// Borrows the sink; failures may leave partial output. Does not flush or close it.
public void WriteVarUInt32(this ITextWriter* output, uint value)
{
    byte[5] bytes;
    nuint written = 0;
    // The maximum-width stack buffer guarantees codec success.
    VarInt::TryWriteUInt32(bytes, 5, value, ref written);
    output.Write((readonly(char)*)bytes, written);
}

/// Sends the canonical shortest UInt64 base-128 representation in one counted Write.
/// Borrows the sink; failures may leave partial output. Does not flush or close it.
public void WriteVarUInt64(this ITextWriter* output, ulong value)
{
    byte[10] bytes;
    nuint written = 0;
    VarInt::TryWriteUInt64(bytes, 10, value, ref written);
    output.Write((readonly(char)*)bytes, written);
}

/// ZigZag-encodes Int32 before canonical base-128 output, including signed extrema.
public void WriteZigZagInt32(this ITextWriter* output, int value)
    => WriteVarUInt32(output, VarInt::ZigZagEncode(value));

/// ZigZag-encodes Int64 before canonical base-128 output, including signed extrema.
public void WriteZigZagInt64(this ITextWriter* output, long value)
    => WriteVarUInt64(output, VarInt::ZigZagEncode(value));
