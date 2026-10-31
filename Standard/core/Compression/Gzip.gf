namespace Standard::Compression;

using Standard::Binary;
using Standard::Hashing;

/// Decodes and validates one gzip member in caller-owned, disjoint counted buffers.
/// Counts report consumed header/body/footer bytes and produced bytes.
public static class Gzip
{
    public enum Status { Done, NeedInput, OutputTooSmall, InvalidData }

    /// A failed checksum or incomplete footer can leave the complete output present.
    /// Done stops after one footer. Concatenated members and trailing bytes are untouched.
    public static Status DecompressMember(readonly(byte)* source, nuint count, byte* output, nuint capacity,
        ref nuint consumed, ref nuint written) nothrow
    {
        consumed = 0; written = 0;
        nuint position = 0;
        defer { consumed = position; }
        if (count < 10) return Status::NeedInput;
        position = 10;
        byte flags = source[3];
        if (source[0] != (byte)31 || source[1] != (byte)139 || source[2] != (byte)8 ||
            (flags & (byte)224) != (byte)0) return Status::InvalidData;

        if ((flags & (byte)4) != (byte)0)
        {
            ushort extra = 0;
            if (!BinaryPrimitives::TryReadUInt16LittleEndian(source + position, count - position, ref extra))
                return Status::NeedInput;
            position += 2;
            if ((nuint)extra > count - position) return Status::NeedInput;
            position += (nuint)extra;
        }
        if ((flags & (byte)8) != (byte)0)
        {
            if (!SkipTerminated(source, count, ref position)) return Status::NeedInput;
        }
        if ((flags & (byte)16) != (byte)0)
        {
            if (!SkipTerminated(source, count, ref position)) return Status::NeedInput;
        }
        if ((flags & (byte)2) != (byte)0)
        {
            ushort expectedHeader = 0;
            if (!BinaryPrimitives::TryReadUInt16LittleEndian(source + position, count - position, ref expectedHeader))
                return Status::NeedInput;
            CRC32 header = new();
            header.Append(source.reinterpret<readonly(char)*>(), position);
            position += 2;
            if ((ushort)header.GetValue() != expectedHeader) return Status::InvalidData;
        }

        nuint bodyConsumed = 0;
        nuint bodyWritten = 0;
        Deflate::Status body = Deflate::Decompress(source + position, count - position, output, capacity,
            ref bodyConsumed, ref bodyWritten);
        position += bodyConsumed;
        written = bodyWritten;
        if (body == Deflate::Status::NeedInput) return Status::NeedInput;
        if (body == Deflate::Status::OutputTooSmall) return Status::OutputTooSmall;
        if (body != Deflate::Status::Done) return Status::InvalidData;

        if (count - position < 8) return Status::NeedInput;
        uint expectedCrc = 0u;
        uint expectedSize = 0u;
        BinaryPrimitives::TryReadUInt32LittleEndian(source + position, 4, ref expectedCrc);
        BinaryPrimitives::TryReadUInt32LittleEndian(source + position + 4, 4, ref expectedSize);
        position += 8;
        CRC32 payload = new();
        if (written != 0) payload.Append(output.reinterpret<readonly(char)*>(), written);
        if (payload.GetValue() != expectedCrc || (uint)written != expectedSize) return Status::InvalidData;
        return Status::Done;
    }

    private static bool SkipTerminated(readonly(byte)* source, nuint count, ref nuint position) nothrow
    {
        while (position < count)
        {
            byte value = source[position];
            position++;
            if (value == (byte)0) return true;
        }
        return false;
    }
}
