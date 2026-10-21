namespace std;

// Canonical unsigned base-128 integers, least-significant group first.
// Metadata refs must not overlap buffers or one another. Zero counts do not
// dereference buffers. No allocation or other std sources are required.
public static class VarInt
{
    public enum Status { Done, NeedMore, Invalid }

    public static nuint GetUInt32Length(uint value) nothrow => GetUInt64Length((ulong)value);

    public static nuint GetUInt64Length(ulong value) nothrow
    {
        nuint length = 1;
        while (value >= 128ul) { value = value >> 7; length++; }
        return length;
    }

    // Failure leaves destination bytes untouched and written=0.
    public static bool TryWriteUInt32(byte* output, nuint capacity, uint value, ref nuint written) nothrow
        => TryWriteUInt64(output, capacity, (ulong)value, ref written);

    public static bool TryWriteUInt64(byte* output, nuint capacity, ulong value, ref nuint written) nothrow
    {
        written = 0;
        nuint length = GetUInt64Length(value);
        if (capacity < length) return false;
        for (nuint i = 0; i < length; i++)
        {
            byte next = (byte)(value & 127ul);
            value = value >> 7;
            if (i + 1 < length) next = next | (byte)128;
            output[i] = next;
        }
        written = length;
        return true;
    }

    // Failure preserves value and sets consumed=0. Trailing bytes are not read.
    // NeedMore retains no state: retry with the prefix and additional bytes.
    public static VarInt::Status ReadUInt32(readonly(byte)* data, nuint count, ref uint value, ref nuint consumed) nothrow
    {
        ulong decoded = 0ul;
        VarInt::Status status = Read(data, count, 5, (byte)15, ref decoded, ref consumed);
        if (status == VarInt::Status::Done) value = (uint)decoded;
        return status;
    }

    public static VarInt::Status ReadUInt64(readonly(byte)* data, nuint count, ref ulong value, ref nuint consumed) nothrow
        => Read(data, count, 10, (byte)1, ref value, ref consumed);

    private static VarInt::Status Read(readonly(byte)* data, nuint count, nuint maximum, byte finalMaximum, ref ulong value, ref nuint consumed) nothrow
    {
        consumed = 0;
        ulong decoded = 0ul;
        int shift = 0;
        for (nuint i = 0; i < maximum; i++)
        {
            if (i == count) return VarInt::Status::NeedMore;
            byte next = data[i];
            if (i + 1 == maximum && next > finalMaximum) return VarInt::Status::Invalid;
            decoded = decoded | ((ulong)(next & (byte)127) << shift);
            if (next < (byte)128)
            {
                // Any extra zero high group makes the encoding noncanonical.
                if (i != 0 && next == (byte)0) return VarInt::Status::Invalid;
                value = decoded;
                consumed = i + 1;
                return VarInt::Status::Done;
            }
            shift += 7;
        }
        return VarInt::Status::Invalid;
    }
}
