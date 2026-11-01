using Standard::Collections;
using Standard::Hashing;
using Standard::IO;
using Standard::Memory;
using Standard::Text;

namespace Standard::Compression;

// Copies borrow the same unchanged archive bytes, independently of the archive index.
public struct ZipArchiveEntry
{
    private Span<readonly(char)> name;
    private Span<readonly(byte)> payload;
    private ushort flags;
    private ushort method;
    private ulong length;
    private uint crc;

    internal ZipArchiveEntry(Span<readonly(char)> nameBytes, Span<readonly(byte)> compressedBytes,
        ushort flags, ushort method, uint length, uint crc)
    {
        name = nameBytes;
        payload = compressedBytes;
        this.flags = flags;
        this.method = method;
        this.length = (ulong)length;
        this.crc = crc;
        if ((flags & (ushort)2048) != 0) ZipNames::ValidateUtf8(nameBytes);
    }

    public Span<readonly(char)> NameBytes { immutable get nothrow => name.AsImmutable(); }
    public bool IsUtf8Name { immutable get nothrow => (flags & (ushort)2048) != 0; }
    public ulong Length { immutable get nothrow => length; }
    public ulong CompressedLength { immutable get nothrow => (ulong)payload.Length; }
    public ushort Method { immutable get nothrow => method; }
    public uint CRC32 { immutable get nothrow => crc; }
    public bool IsDirectory
    {
        immutable get nothrow
        {
            Span<readonly(char)> bytes = name.AsImmutable();
            return bytes.Length != 0 && bytes.Data![bytes.Length - 1] == '/';
        }
    }
    public String FullName
    {
        immutable get
        {
            Span<readonly(char)> bytes = name.AsImmutable();
            if (this.IsUtf8Name)
            {
                if (bytes.IsEmpty) return new String();
                return String::From(bytes.Data!, bytes.Length);
            }
            return ZipNames::DecodeCp437(bytes);
        }
    }

    // Output must be separate from the archive image. Capacity failure writes nothing.
    // Corrupt compressed data or CRC can leave partial or complete output present.
    public immutable nuint Read(byte* output, nuint capacity)
    {
        if (length > (ulong)capacity) throw new IOException("ZIP output buffer is too small");
        Span<readonly(byte)> data = payload.AsImmutable();
        nuint written = 0;
        if (method == 0)
        {
            if ((ulong)data.Length != length) throw new IOException("Stored ZIP entry sizes do not match");
            for (nuint index = 0; index < data.Length; index++) output[index] = data.Data![index];
            written = data.Length;
        }
        else if (method == 8)
        {
            if (data.Length != 0)
            {
                nuint consumed = 0;
                Deflate::Status status = Deflate::Decompress(data.Data!, data.Length, output, (nuint)length,
                    ref consumed, ref written);
                if (status != Deflate::Status::Done || consumed != data.Length || (ulong)written != length)
                    throw new IOException("Invalid ZIP deflate payload");
            }
        }
        else throw new IOException("Unsupported ZIP compression method");
        if ((ulong)written != length) throw new IOException("ZIP output length does not match its header");
        Standard::Hashing::CRC32 checksum = new();
        checksum.Append(output.reinterpret<readonly(char)*>(), written);
        if (checksum.GetValue() != crc) throw new IOException("ZIP CRC32 mismatch");
        return written;
    }

    public immutable Vector<byte> ReadAllBytes(nuint maximumBytes)
    {
        if (length > (ulong)maximumBytes) throw new IOException("ZIP entry exceeds maximum byte count");
        Vector<byte> result = new();
        result.Resize((nuint)length);
        byte empty = 0;
        byte* output = &empty;
        if (result.Count != 0) output = result.AsSpan().Data!;
        this.Read(output, result.Count);
        return move result;
    }
}

// CP437 mapping: Unicode Consortium, MAPPINGS/VENDORS/MICSFT/PC/CP437.TXT.
internal static class ZipNames
{
    private static const ushort[128] HighCp437 = [
        0x00c7, 0x00fc, 0x00e9, 0x00e2, 0x00e4, 0x00e0, 0x00e5, 0x00e7,
        0x00ea, 0x00eb, 0x00e8, 0x00ef, 0x00ee, 0x00ec, 0x00c4, 0x00c5,
        0x00c9, 0x00e6, 0x00c6, 0x00f4, 0x00f6, 0x00f2, 0x00fb, 0x00f9,
        0x00ff, 0x00d6, 0x00dc, 0x00a2, 0x00a3, 0x00a5, 0x20a7, 0x0192,
        0x00e1, 0x00ed, 0x00f3, 0x00fa, 0x00f1, 0x00d1, 0x00aa, 0x00ba,
        0x00bf, 0x2310, 0x00ac, 0x00bd, 0x00bc, 0x00a1, 0x00ab, 0x00bb,
        0x2591, 0x2592, 0x2593, 0x2502, 0x2524, 0x2561, 0x2562, 0x2556,
        0x2555, 0x2563, 0x2551, 0x2557, 0x255d, 0x255c, 0x255b, 0x2510,
        0x2514, 0x2534, 0x252c, 0x251c, 0x2500, 0x253c, 0x255e, 0x255f,
        0x255a, 0x2554, 0x2569, 0x2566, 0x2560, 0x2550, 0x256c, 0x2567,
        0x2568, 0x2564, 0x2565, 0x2559, 0x2558, 0x2552, 0x2553, 0x256b,
        0x256a, 0x2518, 0x250c, 0x2588, 0x2584, 0x258c, 0x2590, 0x2580,
        0x03b1, 0x00df, 0x0393, 0x03c0, 0x03a3, 0x03c3, 0x00b5, 0x03c4,
        0x03a6, 0x0398, 0x03a9, 0x03b4, 0x221e, 0x03c6, 0x03b5, 0x2229,
        0x2261, 0x00b1, 0x2265, 0x2264, 0x2320, 0x2321, 0x00f7, 0x2248,
        0x00b0, 0x2219, 0x00b7, 0x221a, 0x207f, 0x00b2, 0x25a0, 0x00a0
    ];

    internal static void ValidateUtf8(Span<readonly(char)> name)
    {
        nuint at = 0;
        while (at < name.Length)
        {
            uint scalar = 0u;
            nuint consumed = 0;
            if (!Utf8::TryDecode(name.Data! + at, name.Length - at, &scalar, &consumed))
                throw new IOException("Invalid UTF-8 ZIP entry name");
            at += consumed;
        }
    }

    internal static bool Matches(Span<readonly(char)> name, bool utf8, Span<readonly(char)> expected) nothrow
    {
        if (utf8)
        {
            if (name.Length != expected.Length) return false;
            for (nuint index = 0; index < name.Length; index++)
                if (name.Data![index] != expected.Data![index]) return false;
            return true;
        }
        nuint at = 0;
        char[4] encoded;
        for (nuint index = 0; index < name.Length; index++)
        {
            nuint written = 0;
            Utf8::TryEncode(Scalar((byte)name.Data![index]), encoded, 4, &written);
            if (written > expected.Length - at) return false;
            for (nuint offset = 0; offset < written; offset++)
                if (encoded[offset] != expected.Data![at + offset]) return false;
            at += written;
        }
        return at == expected.Length;
    }

    internal static String DecodeCp437(Span<readonly(char)> name)
    {
        nuint length = 0;
        for (nuint index = 0; index < name.Length; index++)
        {
            nuint width = Utf8::EncodedLength(Scalar((byte)name.Data![index]));
            if (width > (nuint)0 - 1 - length - 1) throw new IOException("ZIP entry name is too long");
            length += width;
        }
        String result = new();
        result.Reserve(length);
        for (nuint index = 0; index < name.Length; index++)
        {
            char[4] bytes;
            nuint written = 0;
            Utf8::TryEncode(Scalar((byte)name.Data![index]), bytes, 4, &written);
            result.AppendLiteral(bytes, written);
        }
        return move result;
    }

    private static uint Scalar(byte value) nothrow
    {
        if (value < (byte)128) return (uint)value;
        return (uint)HighCp437[(nuint)value - 128];
    }
}
