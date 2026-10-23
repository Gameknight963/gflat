namespace std;

VarInt::Status ReadVarUInt32(this ref BinaryBufferReader reader, ref uint value) nothrow
{
    nuint remaining = reader.Remaining;
    if (remaining == 0) return VarInt::Status::NeedMore;
    uint decoded = 0u; nuint consumed = 0;
    VarInt::Status status = VarInt::ReadUInt32(reader.Data! + reader.Position, remaining, ref decoded, ref consumed);
    if (status == VarInt::Status::Done) { reader.TrySkip(consumed); value = decoded; }
    return status;
}

VarInt::Status ReadVarUInt64(this ref BinaryBufferReader reader, ref ulong value) nothrow
{
    nuint remaining = reader.Remaining;
    if (remaining == 0) return VarInt::Status::NeedMore;
    ulong decoded = 0ul; nuint consumed = 0;
    VarInt::Status status = VarInt::ReadUInt64(reader.Data! + reader.Position, remaining, ref decoded, ref consumed);
    if (status == VarInt::Status::Done) { reader.TrySkip(consumed); value = decoded; }
    return status;
}

bool TryWriteVarUInt32(this ref BinaryBufferWriter writer, uint value) nothrow
{
    nuint remaining = writer.Remaining;
    if (remaining == 0) return false;
    nuint written = 0;
    if (!VarInt::TryWriteUInt32(writer.Data! + writer.Position, remaining, value, ref written)) return false;
    writer.TrySkip(written);
    return true;
}

bool TryWriteVarUInt64(this ref BinaryBufferWriter writer, ulong value) nothrow
{
    nuint remaining = writer.Remaining;
    if (remaining == 0) return false;
    nuint written = 0;
    if (!VarInt::TryWriteUInt64(writer.Data! + writer.Position, remaining, value, ref written)) return false;
    writer.TrySkip(written);
    return true;
}

VarInt::Status ReadZigZagInt32(this ref BinaryBufferReader reader, ref int value) nothrow
{
    uint encoded = 0u;
    VarInt::Status status = reader.ReadVarUInt32(ref encoded);
    if (status == VarInt::Status::Done) value = VarInt::ZigZagDecode(encoded);
    return status;
}

VarInt::Status ReadZigZagInt64(this ref BinaryBufferReader reader, ref long value) nothrow
{
    ulong encoded = 0ul;
    VarInt::Status status = reader.ReadVarUInt64(ref encoded);
    if (status == VarInt::Status::Done) value = VarInt::ZigZagDecode(encoded);
    return status;
}

bool TryWriteZigZagInt32(this ref BinaryBufferWriter writer, int value) nothrow
    => writer.TryWriteVarUInt32(VarInt::ZigZagEncode(value));

bool TryWriteZigZagInt64(this ref BinaryBufferWriter writer, long value) nothrow
    => writer.TryWriteVarUInt64(VarInt::ZigZagEncode(value));
