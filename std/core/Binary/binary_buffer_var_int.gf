namespace std::Binary;

VarInt::Status ReadVarUInt32(ref BinaryBufferReader reader, ref uint value) nothrow
{
    nuint remaining = reader.Remaining;
    if (remaining == 0) return VarInt::Status::NeedMore;
    uint decoded = 0u; nuint consumed = 0;
    VarInt::Status status = VarInt::ReadUInt32(reader.Data! + reader.Position, remaining, ref decoded, ref consumed);
    if (status == VarInt::Status::Done) { reader.TrySkip(consumed); value = decoded; }
    return status;
}

extension BinaryBufferReader
{
    public VarInt::Status ReadVarUInt32(ref uint value) nothrow
    => std::Binary::ReadVarUInt32(ref *this, ref value);
}

VarInt::Status ReadVarUInt64(ref BinaryBufferReader reader, ref ulong value) nothrow
{
    nuint remaining = reader.Remaining;
    if (remaining == 0) return VarInt::Status::NeedMore;
    ulong decoded = 0ul; nuint consumed = 0;
    VarInt::Status status = VarInt::ReadUInt64(reader.Data! + reader.Position, remaining, ref decoded, ref consumed);
    if (status == VarInt::Status::Done) { reader.TrySkip(consumed); value = decoded; }
    return status;
}

extension BinaryBufferReader
{
    public VarInt::Status ReadVarUInt64(ref ulong value) nothrow
    => std::Binary::ReadVarUInt64(ref *this, ref value);
}

bool TryWriteVarUInt32(ref BinaryBufferWriter writer, uint value) nothrow
{
    nuint remaining = writer.Remaining;
    if (remaining == 0) return false;
    nuint written = 0;
    if (!VarInt::TryWriteUInt32(writer.Data! + writer.Position, remaining, value, ref written)) return false;
    writer.TrySkip(written);
    return true;
}

extension BinaryBufferWriter
{
    public bool TryWriteVarUInt32(uint value) nothrow
    => std::Binary::TryWriteVarUInt32(ref *this, value);
}

bool TryWriteVarUInt64(ref BinaryBufferWriter writer, ulong value) nothrow
{
    nuint remaining = writer.Remaining;
    if (remaining == 0) return false;
    nuint written = 0;
    if (!VarInt::TryWriteUInt64(writer.Data! + writer.Position, remaining, value, ref written)) return false;
    writer.TrySkip(written);
    return true;
}

extension BinaryBufferWriter
{
    public bool TryWriteVarUInt64(ulong value) nothrow
    => std::Binary::TryWriteVarUInt64(ref *this, value);
}

VarInt::Status ReadZigZagInt32(ref BinaryBufferReader reader, ref int value) nothrow
{
    uint encoded = 0u;
    VarInt::Status status = reader.ReadVarUInt32(ref encoded);
    if (status == VarInt::Status::Done) value = VarInt::ZigZagDecode(encoded);
    return status;
}

extension BinaryBufferReader
{
    public VarInt::Status ReadZigZagInt32(ref int value) nothrow
    => std::Binary::ReadZigZagInt32(ref *this, ref value);
}

VarInt::Status ReadZigZagInt64(ref BinaryBufferReader reader, ref long value) nothrow
{
    ulong encoded = 0ul;
    VarInt::Status status = reader.ReadVarUInt64(ref encoded);
    if (status == VarInt::Status::Done) value = VarInt::ZigZagDecode(encoded);
    return status;
}

extension BinaryBufferReader
{
    public VarInt::Status ReadZigZagInt64(ref long value) nothrow
    => std::Binary::ReadZigZagInt64(ref *this, ref value);
}

bool TryWriteZigZagInt32(ref BinaryBufferWriter writer, int value) nothrow
    => writer.TryWriteVarUInt32(VarInt::ZigZagEncode(value));

extension BinaryBufferWriter
{
    public bool TryWriteZigZagInt32(int value) nothrow
    => std::Binary::TryWriteZigZagInt32(ref *this, value);
}

bool TryWriteZigZagInt64(ref BinaryBufferWriter writer, long value) nothrow
    => writer.TryWriteVarUInt64(VarInt::ZigZagEncode(value));

extension BinaryBufferWriter
{
    public bool TryWriteZigZagInt64(long value) nothrow
    => std::Binary::TryWriteZigZagInt64(ref *this, value);
}
