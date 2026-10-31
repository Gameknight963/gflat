using Standard;
using Standard::Binary;
using Standard::Collections;
using Standard::Compression;
using Standard::Hashing;
using Standard::IO;
using Standard::Text;

const nuint MaximumArchiveBytes = 33554432;
const nuint MaximumDecodedBytes = 67108864;

enum Check { Valid, Invalid, Unsupported, TooLarge }

struct Chunk
{
    public uint Type;
    public BinaryBufferReader Data;
    public Chunk() nothrow { Type = 0u; Data = new(); }
}

struct ImageInfo
{
    public uint Width;
    public uint Height;
    public byte Channels;
    public nuint Stride;
    public nuint Expected;
    public nuint RunStart;
    public nuint RunEnd;
    public nuint Compressed;
    public ImageInfo() nothrow
    {
        Width = 0u; Height = 0u; Channels = 0;
        Stride = 0; Expected = 0; RunStart = 0; RunEnd = 0; Compressed = 0;
    }
}

bool Letter(uint value) nothrow
    => (value >= 65u && value <= 90u) || (value >= 97u && value <= 122u);

Check ReadChunk(ref BinaryBufferReader image, ref Chunk chunk) nothrow
{
    chunk = new();
    uint length = 0u; uint expected = 0u;
    if (!image.TryReadUInt32BigEndian(ref length) || length > 2147483647u
        || !image.TryReadUInt32BigEndian(ref chunk.Type)) return Check::Invalid;
    readonly(byte)* type = image.Data! + image.Position - 4;
    for (int shift = 24; shift >= 0; shift -= 8)
        if (!Letter((chunk.Type >> shift) & 255u)) return Check::Invalid;
    if (!image.TryReadSlice((nuint)length, ref chunk.Data) || !image.TryReadUInt32BigEndian(ref expected))
        return Check::Invalid;
    CRC32 crc = new();
    crc.Append(type.reinterpret<readonly(char)*>(), 4);
    if (length != 0u) crc.Append((chunk.Data.Data!).reinterpret<readonly(char)*>(), (nuint)length);
    if (crc.GetValue() != expected) return Check::Invalid;
    // Future chunk-name versions are unsupported, rather than called universally corrupt.
    if ((chunk.Type & 0x00002000u) != 0u) return Check::Unsupported;
    return Check::Valid;
}

bool LegalMode(byte depth, byte color) nothrow
{
    if (color == (byte)0) return depth == (byte)1 || depth == (byte)2 || depth == (byte)4 || depth == (byte)8 || depth == (byte)16;
    if (color == (byte)3) return depth == (byte)1 || depth == (byte)2 || depth == (byte)4 || depth == (byte)8;
    if (color == (byte)2 || color == (byte)4 || color == (byte)6) return depth == (byte)8 || depth == (byte)16;
    return false;
}

Check ReadHeader(BinaryBufferReader data, ref ImageInfo info) nothrow
{
    byte depth = 0; byte color = 0; byte compression = 0; byte filter = 0; byte interlace = 0;
    if (data.Length != 13 || !data.TryReadUInt32BigEndian(ref info.Width) || !data.TryReadUInt32BigEndian(ref info.Height)
        || !data.TryReadByte(ref depth) || !data.TryReadByte(ref color) || !data.TryReadByte(ref compression)
        || !data.TryReadByte(ref filter) || !data.TryReadByte(ref interlace)) return Check::Invalid;
    if (info.Width == 0u || info.Height == 0u || info.Width > 2147483647u || info.Height > 2147483647u
        || !LegalMode(depth, color) || compression != (byte)0 || filter != (byte)0 || interlace > (byte)1) return Check::Invalid;
    if (depth != (byte)8 || (color != (byte)2 && color != (byte)6) || interlace != (byte)0) return Check::Unsupported;
    info.Channels = (byte)3;
    if (color == (byte)6) info.Channels = (byte)4;
    if ((nuint)info.Width > (MaximumDecodedBytes - 1) / (nuint)info.Channels) return Check::TooLarge;
    info.Stride = (nuint)info.Width * (nuint)info.Channels + 1;
    if ((nuint)info.Height > MaximumDecodedBytes / info.Stride) return Check::TooLarge;
    info.Expected = info.Stride * (nuint)info.Height;
    return Check::Valid;
}

Check Scan(BinaryBufferReader image, ref ImageInfo info) nothrow
{
    byte[8] signature = [(byte)137,(byte)80,(byte)78,(byte)71,(byte)13,(byte)10,(byte)26,(byte)10];
    for (nuint i = 0; i < 8; i++)
    {
        byte value = 0;
        if (!image.TryReadByte(ref value) || value != signature[i]) return Check::Invalid;
    }
    bool header = false; bool palette = false; bool data = false; bool ended = false;
    Chunk chunk = new();
    while (image.Remaining != 0)
    {
        nuint start = image.Position;
        Check status = ReadChunk(ref image, ref chunk);
        if (status != Check::Valid) return status;
        if (!header)
        {
            if (chunk.Type != 0x49484452u) return Check::Invalid;
            status = ReadHeader(chunk.Data, ref info);
            if (status != Check::Valid) return status;
            header = true;
            continue;
        }
        if (chunk.Type == 0x49484452u) return Check::Invalid;
        if (chunk.Type == 0x49444154u)
        {
            if (ended) return Check::Invalid;
            if (!data) info.RunStart = start;
            data = true; info.RunEnd = image.Position;
            if (chunk.Data.Length > MaximumArchiveBytes - info.Compressed) return Check::TooLarge;
            info.Compressed += chunk.Data.Length;
            continue;
        }
        if (data) ended = true;
        if (chunk.Type == 0x504c5445u)
        {
            if (palette || data || chunk.Data.Length == 0 || chunk.Data.Length > 768 || chunk.Data.Length % 3 != 0)
                return Check::Invalid;
            palette = true; continue;
        }
        if (chunk.Type == 0x49454e44u)
        {
            if (!data || chunk.Data.Length != 0 || image.Remaining != 0) return Check::Invalid;
            return Check::Valid;
        }
        if (chunk.Type == 0x6163544cu || chunk.Type == 0x6663544cu || chunk.Type == 0x66644154u)
            return Check::Unsupported;
        if ((chunk.Type & 0x20000000u) == 0u) return Check::Unsupported;
        // Ordinary ancillary payload semantics are deliberately outside this example.
    }
    return Check::Invalid;
}

Check JoinData(BinaryBufferReader image, ImageInfo info, ref Vector<byte> joined)
{
    joined.Resize(info.Compressed);
    if (!image.TrySeek(info.RunStart)) return Check::Invalid;
    nuint copied = 0;
    Chunk chunk = new();
    while (image.Position < info.RunEnd)
    {
        Check status = ReadChunk(ref image, ref chunk);
        if (status != Check::Valid) return status;
        if (chunk.Type != 0x49444154u || chunk.Data.Length > joined.Count - copied) return Check::Invalid;
        if (chunk.Data.Length != 0)
        {
            byte* destination = joined.AsSpan().Data!;
            readonly(byte)* source = chunk.Data.Data!;
            for (nuint i = 0; i < chunk.Data.Length; i++) destination[copied + i] = source[i];
        }
        copied += chunk.Data.Length;
    }
    if (image.Position != info.RunEnd || copied != joined.Count) return Check::Invalid;
    return Check::Valid;
}

Check CheckData(ref Vector<byte> compressed, ImageInfo info)
{
    Vector<byte> output = new();
    output.Resize(info.Expected);
    byte dummy = 0;
    readonly(byte)* source = &dummy;
    if (compressed.Count != 0) source = compressed.AsSpan().Data!;
    byte* destination = output.AsSpan().Data!;
    nuint consumed = 0; nuint written = 0;
    if (Zlib::Decompress(source, compressed.Count, destination, output.Count, ref consumed, ref written) != Zlib::Status::Done
        || consumed > compressed.Count || written != info.Expected) return Check::Invalid;
    // PNG decoders tolerate unused trailing bytes after the first complete zlib stream.
    nuint offset = 0;
    for (uint row = 0u; row < info.Height; row++)
    {
        if (destination[offset] > (byte)4) return Check::Invalid;
        offset += info.Stride;
    }
    return Check::Valid;
}

void Require(Check status)
{
    if (status == Check::Unsupported) throw new IOException("Unsupported PNG image mode or chunk");
    if (status == Check::TooLarge) throw new IOException("PNG file or decoded image limit exceeded");
    if (status != Check::Valid) throw new IOException("Invalid PNG structure or image data");
}

ImageInfo VerifyFile(readonly(char)* path)
{
    Vector<byte> bytes = FileBytes::ReadAll(path, MaximumArchiveBytes);
    BinaryBufferReader image = new();
    if (bytes.Count != 0) image = new(bytes.AsSpan().Data!, bytes.Count);
    ImageInfo info = new();
    Require(Scan(image, ref info));
    Vector<byte> compressed = new();
    Require(JoinData(image, info, ref compressed));
    Require(CheckData(ref compressed, info));
    return info;
}

int main(int argc, char** argv)
{
    if (argc != 2)
    {
        try { Console::Error.WriteLine("Usage: png-verify <file>"); Console::FlushError(); }
        catch (ref Exception ignored) { }
        return 2;
    }
    try
    {
        ImageInfo info = VerifyFile(argv[1]);
        if (info.Channels == (byte)3) Console::Write("Verified RGB8 image data: ");
        else Console::Write("Verified RGBA8 image data: ");
        info.Width.Format(Console::Out); Console::Write(" x "); info.Height.Format(Console::Out);
        Console::WriteLine(); Console::Flush();
    }
    catch (ref Exception error)
    {
        try { Console::Error.Write("png-verify: ", 12); Console::Error.WriteLine(error.GetMessage()); Console::FlushError(); }
        catch (ref Exception ignored) { }
        return 1;
    }
    return 0;
}
