using std::Binary;
namespace std::IO;

// Frame one encoded value without consuming a byte from the following field.
// Canonical validation remains entirely in the existing VarInt codec.
private nuint ReadVarIntBytes(IByteReader* input, byte* bytes, nuint maximum)
{
    for (nuint index = 0; index < maximum; index++)
    {
        ReadExactly(input, bytes.reinterpret<char*>() + index, 1);
        if (bytes[index] < (byte)128) return index + 1;
    }
    return maximum;
}

/// Reads one canonical unsigned base-128 UInt32, borrowing the source.
/// Makes at most five one-byte reads, with no prefetch. EOF or errors may consume a prefix.
public uint ReadVarUInt32(IByteReader* input)
{
    byte[5] bytes;
    nuint count = ReadVarIntBytes(input, bytes, 5);
    uint value = 0u;
    nuint consumed = 0;
    if (VarInt::ReadUInt32(bytes, count, ref value, ref consumed) != VarInt::Status::Done)
        throw new IOException("Invalid UInt32 variable-length integer");
    return value;
}

extension IByteReader
{
    public uint ReadVarUInt32()
    => std::IO::ReadVarUInt32(this);
}

/// Reads one canonical unsigned base-128 UInt64, borrowing the source.
/// Makes at most ten one-byte reads, with no prefetch. EOF or errors may consume a prefix.
public ulong ReadVarUInt64(IByteReader* input)
{
    byte[10] bytes;
    nuint count = ReadVarIntBytes(input, bytes, 10);
    ulong value = 0ul;
    nuint consumed = 0;
    if (VarInt::ReadUInt64(bytes, count, ref value, ref consumed) != VarInt::Status::Done)
        throw new IOException("Invalid UInt64 variable-length integer");
    return value;
}

extension IByteReader
{
    public ulong ReadVarUInt64()
    => std::IO::ReadVarUInt64(this);
}

/// Decodes a canonical UInt32 varint using ZigZag, not signed LEB128 or protobuf int32.
public int ReadZigZagInt32(IByteReader* input)
    => VarInt::ZigZagDecode(ReadVarUInt32(input));

extension IByteReader
{
    public int ReadZigZagInt32()
    => std::IO::ReadZigZagInt32(this);
}

/// Decodes a canonical UInt64 varint using ZigZag, not signed LEB128 or protobuf int64.
public long ReadZigZagInt64(IByteReader* input)
    => VarInt::ZigZagDecode(ReadVarUInt64(input));

extension IByteReader
{
    public long ReadZigZagInt64()
    => std::IO::ReadZigZagInt64(this);
}
