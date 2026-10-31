namespace Standard::Compression;

using Standard::Binary;
using Standard::Hashing;

/// Decodes and validates one RFC 1950 stream without allocating or retaining buffers.
public static class Zlib
{
    public enum Status { Done, NeedInput, OutputTooSmall, InvalidData, UnsupportedDictionary }

    /// Borrows disjoint counted source/output buffers and independent count outputs.
    /// Decodes the complete stream in one call. Checksum failures can leave all output present.
    public static Status Decompress(readonly(byte)* source, nuint count, byte* output, nuint capacity,
        ref nuint consumed, ref nuint written) nothrow
    {
        consumed = 0; written = 0;
        nuint position = 0;
        defer { consumed = position; }
        if (count < 2) return Status::NeedInput;
        position = 2;
        uint header = ((uint)source[0] << 8) | (uint)source[1];
        uint information = (uint)source[0] >> 4;
        if (((uint)source[0] & 15u) != 8u || information > 7u || header % 31u != 0u)
            return Status::InvalidData;
        if (((uint)source[1] & 32u) != 0u)
        {
            if (count - position < 4) return Status::NeedInput;
            position += 4;
            return Status::UnsupportedDictionary;
        }

        nuint maximumDistance = (nuint)1 << (int)(information + 8u);
        nuint bodyConsumed = 0;
        nuint bodyWritten = 0;
        Deflate::Status body = Deflate::DecompressWithWindow(source + position, count - position, output, capacity,
            maximumDistance, ref bodyConsumed, ref bodyWritten);
        position += bodyConsumed;
        written = bodyWritten;
        if (body == Deflate::Status::NeedInput) return Status::NeedInput;
        if (body == Deflate::Status::OutputTooSmall) return Status::OutputTooSmall;
        if (body != Deflate::Status::Done) return Status::InvalidData;

        if (count - position < 4) return Status::NeedInput;
        uint expected = 0u;
        BinaryPrimitives::TryReadUInt32BigEndian(source + position, 4, ref expected);
        position += 4;
        Adler32 checksum = new();
        if (written != 0) checksum.Append(output.reinterpret<readonly(char)*>(), written);
        if (checksum.GetValue() != expected) return Status::InvalidData;
        return Status::Done;
    }
}
