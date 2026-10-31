using Standard;
using Standard::Binary;
using Standard::Collections;
using Standard::Compression;
using Standard::Hashing;
using Standard::IO;
using Standard::Text;

const nuint MaximumArchiveBytes = 134217728;
const nuint MaximumEntryBytes = 16777216;
const nuint MaximumTotalBytes = 134217728;

enum Check { Valid, Invalid, Unsupported, TooLarge }

// Example-local metadata borrows the unchanged complete archive image.
struct Entry
{
    public ushort Version;
    public ushort Flags;
    public ushort Method;
    public ushort Time;
    public ushort Date;
    public ushort Disk;
    public uint Crc;
    public uint Compressed;
    public uint Uncompressed;
    public uint Offset;
    public BinaryBufferReader Name;
    public BinaryBufferReader Extra;

    public Entry() nothrow
    {
        Version = 0; Flags = 0; Method = 0; Time = 0; Date = 0; Disk = 0;
        Crc = 0u; Compressed = 0u; Uncompressed = 0u; Offset = 0u;
        Name = new(); Extra = new();
    }
}

bool ReadEntry(ref BinaryBufferReader directory, ref Entry entry) nothrow
{
    entry = new();
    uint signature = 0u;
    ushort nameLength = 0; ushort extraLength = 0; ushort commentLength = 0;
    return directory.TryReadUInt32LittleEndian(ref signature) && signature == 0x02014b50u
        && directory.TrySkip(2) && directory.TryReadUInt16LittleEndian(ref entry.Version)
        && directory.TryReadUInt16LittleEndian(ref entry.Flags)
        && directory.TryReadUInt16LittleEndian(ref entry.Method)
        && directory.TryReadUInt16LittleEndian(ref entry.Time)
        && directory.TryReadUInt16LittleEndian(ref entry.Date)
        && directory.TryReadUInt32LittleEndian(ref entry.Crc)
        && directory.TryReadUInt32LittleEndian(ref entry.Compressed)
        && directory.TryReadUInt32LittleEndian(ref entry.Uncompressed)
        && directory.TryReadUInt16LittleEndian(ref nameLength)
        && directory.TryReadUInt16LittleEndian(ref extraLength)
        && directory.TryReadUInt16LittleEndian(ref commentLength)
        && directory.TryReadUInt16LittleEndian(ref entry.Disk)
        && directory.TrySkip(6) && directory.TryReadUInt32LittleEndian(ref entry.Offset)
        && directory.TryReadSlice((nuint)nameLength, ref entry.Name)
        && directory.TryReadSlice((nuint)extraLength, ref entry.Extra)
        && directory.TrySkip((nuint)commentLength);
}

Check TryDirectory(BinaryBufferReader image, nuint at, ref BinaryBufferReader directory,
    ref ushort entries, ref nuint offset, ref nuint remainingScan) nothrow
{
    directory = new(); entries = 0; offset = 0;
    uint signature = 0u; uint size = 0u; uint start = 0u;
    ushort disk = 0; ushort startDisk = 0; ushort onDisk = 0; ushort commentLength = 0;
    if (!image.TrySeek(at) || !image.TryReadUInt32LittleEndian(ref signature) || signature != 0x06054b50u
        || !image.TryReadUInt16LittleEndian(ref disk) || !image.TryReadUInt16LittleEndian(ref startDisk)
        || !image.TryReadUInt16LittleEndian(ref onDisk) || !image.TryReadUInt16LittleEndian(ref entries)
        || !image.TryReadUInt32LittleEndian(ref size) || !image.TryReadUInt32LittleEndian(ref start)
        || !image.TryReadUInt16LittleEndian(ref commentLength)) return Check::Invalid;
    if (disk != 0 || startDisk != 0 || onDisk != entries || entries == (ushort)65535
        || size == 0xffffffffu || start == 0xffffffffu || image.Remaining != (nuint)commentLength) return Check::Invalid;
    if ((nuint)start > at || (nuint)size != at - (nuint)start || (nuint)entries > (nuint)size / 46) return Check::Invalid;
    if (!image.TrySeek((nuint)start) || !image.TryReadSlice((nuint)size, ref directory)) return Check::Invalid;
    if ((nuint)size > remainingScan) return Check::TooLarge;
    remainingScan -= (nuint)size;
    BinaryBufferReader walk = directory;
    Entry entry = new();
    for (nuint i = 0; i < (nuint)entries; i++) if (!ReadEntry(ref walk, ref entry)) return Check::Invalid;
    if (walk.Remaining != 0) return Check::Invalid;
    offset = (nuint)start;
    return Check::Valid;
}

Check FindDirectory(BinaryBufferReader image, ref BinaryBufferReader directory,
    ref ushort entries, ref nuint offset, ref nuint remainingScan) nothrow
{
    directory = new(); entries = 0; offset = 0;
    if (image.Length < 22) return Check::Invalid;
    nuint at = image.Length - 22;
    nuint lower = 0;
    if (at > 65535) lower = at - 65535;
    while (true)
    {
        Check status = TryDirectory(image, at, ref directory, ref entries, ref offset, ref remainingScan);
        if (status == Check::Valid || status == Check::TooLarge) return status;
        if (at == lower) return Check::Invalid;
        at--;
    }
}

Check CheckExtra(BinaryBufferReader extra) nothrow
{
    while (extra.Remaining != 0)
    {
        ushort id = 0; ushort count = 0;
        if (!extra.TryReadUInt16LittleEndian(ref id) || !extra.TryReadUInt16LittleEndian(ref count)
            || !extra.TrySkip((nuint)count)) return Check::Invalid;
        if (id == 1) return Check::Unsupported;
    }
    return Check::Valid;
}

Check CheckMetadata(Entry entry) nothrow
{
    if (entry.Version < 10) return Check::Invalid;
    if (entry.Version > 20 || entry.Disk != 0 || entry.Compressed == 0xffffffffu
        || entry.Uncompressed == 0xffffffffu || entry.Offset == 0xffffffffu
        || (entry.Flags & (ushort)0xf7f1) != 0 || (entry.Method != 0 && entry.Method != 8)) return Check::Unsupported;
    if (entry.Method == 0 && (entry.Flags & (ushort)6) != 0) return Check::Unsupported;
    if (entry.Method == 8 && entry.Version < 20) return Check::Invalid;
    if (entry.Method == 0 && entry.Compressed != entry.Uncompressed) return Check::Invalid;
    return CheckExtra(entry.Extra);
}

bool SameName(BinaryBufferReader left, BinaryBufferReader right) nothrow
{
    if (left.Length != right.Length) return false;
    for (nuint i = 0; i < left.Length; i++) if (left.Data![i] != right.Data![i]) return false;
    return true;
}

bool DescriptorMatches(BinaryBufferReader input, Entry entry, bool signature) nothrow
{
    uint marker = 0u; uint crc = 0u; uint compressed = 0u; uint uncompressed = 0u;
    if (signature && (!input.TryReadUInt32LittleEndian(ref marker) || marker != 0x08074b50u)) return false;
    return input.TryReadUInt32LittleEndian(ref crc) && input.TryReadUInt32LittleEndian(ref compressed)
        && input.TryReadUInt32LittleEndian(ref uncompressed)
        && crc == entry.Crc && compressed == entry.Compressed && uncompressed == entry.Uncompressed;
}

Check LocalPayload(BinaryBufferReader image, nuint centralOffset, Entry entry, ref BinaryBufferReader payload,
    ref nuint remainingScan) nothrow
{
    payload = new();
    BinaryBufferReader local = new();
    if (!image.TrySeek(0) || !image.TryReadSlice(centralOffset, ref local) || !local.TrySeek((nuint)entry.Offset)) return Check::Invalid;
    uint marker = 0u; uint crc = 0u; uint compressed = 0u; uint uncompressed = 0u;
    ushort version = 0; ushort flags = 0; ushort method = 0; ushort time = 0; ushort date = 0;
    ushort nameLength = 0; ushort extraLength = 0;
    if (!local.TryReadUInt32LittleEndian(ref marker) || marker != 0x04034b50u
        || !local.TryReadUInt16LittleEndian(ref version) || !local.TryReadUInt16LittleEndian(ref flags)
        || !local.TryReadUInt16LittleEndian(ref method) || !local.TryReadUInt16LittleEndian(ref time)
        || !local.TryReadUInt16LittleEndian(ref date) || !local.TryReadUInt32LittleEndian(ref crc)
        || !local.TryReadUInt32LittleEndian(ref compressed) || !local.TryReadUInt32LittleEndian(ref uncompressed)
        || !local.TryReadUInt16LittleEndian(ref nameLength) || !local.TryReadUInt16LittleEndian(ref extraLength)) return Check::Invalid;
    if (version != entry.Version || flags != entry.Flags || method != entry.Method || time != entry.Time || date != entry.Date) return Check::Invalid;
    if ((flags & (ushort)8) == 0)
    {
        if (crc != entry.Crc || compressed != entry.Compressed || uncompressed != entry.Uncompressed) return Check::Invalid;
    }
    else if (crc != 0u || compressed != 0u || uncompressed != 0u) return Check::Invalid;
    nuint metadataBytes = 30 + (nuint)nameLength + (nuint)extraLength;
    if (metadataBytes > remainingScan) return Check::TooLarge;
    remainingScan -= metadataBytes;
    BinaryBufferReader name = new(); BinaryBufferReader extra = new();
    if (!local.TryReadSlice((nuint)nameLength, ref name) || !local.TryReadSlice((nuint)extraLength, ref extra)
        || !SameName(name, entry.Name)) return Check::Invalid;
    Check status = CheckExtra(extra);
    if (status != Check::Valid) return status;
    if (!local.TryReadSlice((nuint)entry.Compressed, ref payload)) return Check::Invalid;
    if ((flags & (ushort)8) != 0 && !DescriptorMatches(local, entry, true) && !DescriptorMatches(local, entry, false)) return Check::Invalid;
    return Check::Valid;
}

Check VerifyPayload(BinaryBufferReader payload, Entry entry)
{
    CRC32 crc = new();
    // ZIP permits an empty file with no data, even when its method is 8.
    // This container case is distinct from decoding an empty raw DEFLATE stream.
    if (entry.Compressed == 0u)
    {
        if (entry.Uncompressed != 0u || entry.Crc != 0u) return Check::Invalid;
        return Check::Valid;
    }
    if (entry.Method == 0)
    {
        if (payload.Length != 0) crc.Append((payload.Data!).reinterpret<readonly(char)*>(), payload.Length);
        if (crc.GetValue() != entry.Crc) return Check::Invalid;
        return Check::Valid;
    }
    Vector<byte> output = new();
    output.Resize((nuint)entry.Uncompressed);
    byte dummy = 0;
    byte* storage = &dummy;
    readonly(byte)* source = &dummy;
    if (output.Count != 0) storage = output.AsSpan().Data!;
    if (payload.Length != 0) source = payload.Data!;
    nuint consumed = 0; nuint written = 0;
    if (Deflate::Decompress(source, payload.Length, storage, output.Count, ref consumed, ref written) != Deflate::Status::Done
        || consumed != payload.Length || written != (nuint)entry.Uncompressed) return Check::Invalid;
    if (written != 0) crc.Append(storage.reinterpret<readonly(char)*>(), written);
    if (crc.GetValue() != entry.Crc) return Check::Invalid;
    return Check::Valid;
}

void Require(Check status)
{
    if (status == Check::Unsupported) throw new IOException("Unsupported ZIP feature");
    if (status == Check::TooLarge) throw new IOException("ZIP work or output limit exceeded");
    if (status != Check::Valid) throw new IOException("Invalid ZIP entry or payload");
}

nuint VerifyFile(readonly(char)* path)
{
    Vector<byte> bytes = FileBytes::ReadAll(path, MaximumArchiveBytes);
    BinaryBufferReader image = new();
    if (bytes.Count != 0) image = new(bytes.AsSpan().Data!, bytes.Count);
    BinaryBufferReader directory = new(); ushort entries = 0; nuint centralOffset = 0;
    nuint remainingMetadata = MaximumArchiveBytes;
    Check found = FindDirectory(image, ref directory, ref entries, ref centralOffset, ref remainingMetadata);
    if (found == Check::TooLarge) Require(found);
    if (found != Check::Valid)
        throw new IOException("Unsupported or invalid ZIP32 central directory");
    // Charge every entry, including duplicate references, before allocating or decoding.
    nuint remainingOutput = MaximumTotalBytes; nuint remainingInput = MaximumArchiveBytes;
    BinaryBufferReader metadata = directory;
    Entry entry = new();
    for (nuint i = 0; i < (nuint)entries; i++)
    {
        if (!ReadEntry(ref metadata, ref entry)) Require(Check::Invalid);
        Require(CheckMetadata(entry));
        if ((nuint)entry.Uncompressed > MaximumEntryBytes || (nuint)entry.Uncompressed > remainingOutput
            || (nuint)entry.Compressed > remainingInput) Require(Check::TooLarge);
        remainingOutput -= (nuint)entry.Uncompressed;
        remainingInput -= (nuint)entry.Compressed;
    }
    for (nuint i = 0; i < (nuint)entries; i++)
    {
        if (!ReadEntry(ref directory, ref entry)) Require(Check::Invalid);
        BinaryBufferReader payload = new();
        Require(LocalPayload(image, centralOffset, entry, ref payload, ref remainingMetadata));
        Require(VerifyPayload(payload, entry));
    }
    return (nuint)entries;
}

int main(int argc, char** argv)
{
    if (argc != 2)
    {
        try { Console::Error.WriteLine("Usage: zip-verify <file>"); Console::FlushError(); }
        catch (ref Exception ignored) { }
        return 2;
    }
    try
    {
        nuint entries = VerifyFile(argv[1]);
        Console::Write("Verified "); entries.Format(Console::Out); Console::WriteLine(" entries"); Console::Flush();
    }
    catch (ref Exception error)
    {
        try { Console::Error.Write("zip-verify: ", 12); Console::Error.WriteLine(error.GetMessage()); Console::FlushError(); }
        catch (ref Exception ignored) { }
        return 1;
    }
    return 0;
}
