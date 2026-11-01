using Standard::Binary;
using Standard::Collections;
using Standard::IO;
using Standard::Memory;

namespace Standard::Compression;

// Owns an entry index while borrowing the unchanged complete archive image.
public struct ZipArchive
{
    private Vector<ZipArchiveEntry> entries;

    public ZipArchive(readonly(byte)* image, nuint count)
    {
        entries = new();
        this.Parse(new BinaryBufferReader(image, count));
    }
    public ZipArchive(Span<readonly(byte)> image)
    {
        entries = new();
        BinaryBufferReader source = new();
        if (!image.IsEmpty) source = new(image.Data!, image.Length);
        this.Parse(source);
    }
    public copy ZipArchive(immutable(ZipArchive)* source)
    {
        entries = new();
        entries.Reserve(source.entries.Count);
        for (nuint index = 0; index < source.entries.Count; index++) entries.Add(source.entries[index]);
    }

    public nuint Count { immutable get nothrow => entries.Count; }
    public Span<readonly(ZipArchiveEntry)> Entries { readonly get => entries.AsReadOnly(); }

    private void Parse(BinaryBufferReader image)
    {
        nuint maximum = (nuint)0 - (nuint)1;
        nuint budget = maximum;
        if (image.Length <= (maximum - 65536) / 4) budget = image.Length * 4 + 65536;
        BinaryBufferReader directory = new();
        ushort count = 0;
        nuint centralOffset = 0;
        if (!FindDirectory(image, ref directory, ref count, ref centralOffset, ref budget))
            throw new IOException("Unsupported or invalid ZIP32 central directory");
        entries.Reserve((nuint)count);
        for (nuint index = 0; index < (nuint)count; index++)
        {
            Record record = new();
            if (!ReadRecord(ref directory, ref record)) throw new IOException("Invalid ZIP central entry");
            CheckMetadata(record);
            BinaryBufferReader payload = LocalPayload(image, centralOffset, record, ref budget);
            Span<readonly(char)> name = new();
            Span<readonly(byte)> compressed = new();
            if (record.Name.Length != 0)
                name = new((record.Name.Data!).reinterpret<readonly(char)*>(), record.Name.Length);
            if (payload.Length != 0) compressed = new(payload.Data!, payload.Length);
            entries.Add(new ZipArchiveEntry(name, compressed, record.Flags, record.Method, record.Uncompressed, record.Crc));
        }
    }

    private struct Record
    {
        public ushort Version;
        public ushort Flags;
        public ushort Method;
        public uint DateTime;
        public uint Crc;
        public uint Compressed;
        public uint Uncompressed;
        public uint Offset;
        public ushort Disk;
        public BinaryBufferReader Name;
        public BinaryBufferReader Extra;
        public Record() nothrow
        {
            Version = 0; Flags = 0; Method = 0; DateTime = 0u; Crc = 0u;
            Compressed = 0u; Uncompressed = 0u; Offset = 0u; Disk = 0;
            Name = new(); Extra = new();
        }
    }

    private static bool ReadRecord(ref BinaryBufferReader directory, ref Record record) nothrow
    {
        record = new();
        uint signature = 0u;
        ushort nameLength = 0;
        ushort extraLength = 0;
        ushort commentLength = 0;
        return directory.TryReadUInt32LittleEndian(ref signature) && signature == 0x02014b50u
            && directory.TrySkip(2) && directory.TryReadUInt16LittleEndian(ref record.Version)
            && directory.TryReadUInt16LittleEndian(ref record.Flags) && directory.TryReadUInt16LittleEndian(ref record.Method)
            && directory.TryReadUInt32LittleEndian(ref record.DateTime) && directory.TryReadUInt32LittleEndian(ref record.Crc)
            && directory.TryReadUInt32LittleEndian(ref record.Compressed) && directory.TryReadUInt32LittleEndian(ref record.Uncompressed)
            && directory.TryReadUInt16LittleEndian(ref nameLength) && directory.TryReadUInt16LittleEndian(ref extraLength)
            && directory.TryReadUInt16LittleEndian(ref commentLength) && directory.TryReadUInt16LittleEndian(ref record.Disk)
            && directory.TrySkip(6) && directory.TryReadUInt32LittleEndian(ref record.Offset)
            && directory.TryReadSlice((nuint)nameLength, ref record.Name)
            && directory.TryReadSlice((nuint)extraLength, ref record.Extra)
            && directory.TrySkip((nuint)commentLength);
    }

    private static void Charge(nuint amount, ref nuint budget)
    {
        if (amount > budget) throw new IOException("ZIP metadata scan limit exceeded");
        budget -= amount;
    }

    private static bool TryDirectory(BinaryBufferReader image, nuint at, ref BinaryBufferReader directory,
        ref ushort count, ref nuint centralOffset, ref nuint budget)
    {
        uint signature = 0u;
        if (!image.TrySeek(at) || !image.TryReadUInt32LittleEndian(ref signature) || signature != 0x06054b50u) return false;
        Charge(22, ref budget);
        ushort disk = 0; ushort startDisk = 0; ushort onDisk = 0; ushort commentLength = 0;
        uint size = 0u; uint offset = 0u;
        if (!image.TryReadUInt16LittleEndian(ref disk) || !image.TryReadUInt16LittleEndian(ref startDisk)
            || !image.TryReadUInt16LittleEndian(ref onDisk) || !image.TryReadUInt16LittleEndian(ref count)
            || !image.TryReadUInt32LittleEndian(ref size) || !image.TryReadUInt32LittleEndian(ref offset)
            || !image.TryReadUInt16LittleEndian(ref commentLength) || image.Remaining != (nuint)commentLength) return false;
        if (disk != 0 || startDisk != 0 || onDisk != count || count == (ushort)65535
            || size == 0xffffffffu || offset == 0xffffffffu) return false;
        if ((nuint)offset > at || (nuint)size != at - (nuint)offset || (nuint)count > (nuint)size / 46) return false;
        if (!image.TrySeek((nuint)offset) || !image.TryReadSlice((nuint)size, ref directory)) return false;
        Charge((nuint)size, ref budget);
        BinaryBufferReader probe = directory;
        Record record = new();
        for (nuint index = 0; index < (nuint)count; index++)
            if (!ReadRecord(ref probe, ref record)) return false;
        if (probe.Remaining != 0) return false;
        centralOffset = (nuint)offset;
        return true;
    }

    private static bool FindDirectory(BinaryBufferReader image, ref BinaryBufferReader directory,
        ref ushort count, ref nuint offset, ref nuint budget)
    {
        directory = new(); count = 0; offset = 0;
        if (image.Length < 22) return false;
        nuint at = image.Length - 22;
        nuint lower = 0;
        if (at > 65535) lower = at - 65535;
        while (true)
        {
            if (TryDirectory(image, at, ref directory, ref count, ref offset, ref budget)) return true;
            if (at == lower) return false;
            at--;
        }
    }

    private static void CheckExtra(BinaryBufferReader extra)
    {
        while (extra.Remaining != 0)
        {
            ushort id = 0; ushort length = 0;
            if (!extra.TryReadUInt16LittleEndian(ref id) || !extra.TryReadUInt16LittleEndian(ref length)
                || !extra.TrySkip((nuint)length)) throw new IOException("Invalid ZIP extra field");
            if (id == 1) throw new IOException("ZIP64 entries are not supported");
        }
    }

    private static void CheckMetadata(Record record)
    {
        if (record.Version < 10 || (record.Method == 8 && record.Version < 20))
            throw new IOException("Invalid ZIP entry version");
        if (record.Version > 20) throw new IOException("Unsupported ZIP entry version");
        if (record.Disk != 0) throw new IOException("Multi-disk ZIP archives are not supported");
        if (record.Compressed == 0xffffffffu || record.Uncompressed == 0xffffffffu || record.Offset == 0xffffffffu)
            throw new IOException("ZIP64 entries are not supported");
        if ((record.Flags & (ushort)0x2041) != 0) throw new IOException("Encrypted ZIP entries are not supported");
        if ((record.Flags & (ushort)0xf7f1) != 0 || (record.Method == 0 && (record.Flags & (ushort)6) != 0))
            throw new IOException("Unsupported ZIP entry flags");
        if (record.Method != 0 && record.Method != 8) throw new IOException("Unsupported ZIP compression method");
        if (record.Method == 0 && record.Compressed != record.Uncompressed)
            throw new IOException("Stored ZIP entry sizes do not match");
        if (record.Compressed == 0u && (record.Uncompressed != 0u || record.Crc != 0u))
            throw new IOException("Invalid empty ZIP entry");
        CheckExtra(record.Extra);
    }

    private static bool SameName(BinaryBufferReader first, BinaryBufferReader second) nothrow
    {
        if (first.Length != second.Length) return false;
        for (nuint index = 0; index < first.Length; index++)
            if (first.Data![index] != second.Data![index]) return false;
        return true;
    }

    private static bool DescriptorMatches(BinaryBufferReader input, Record record, bool signature) nothrow
    {
        uint marker = 0u; uint crc = 0u; uint compressed = 0u; uint uncompressed = 0u;
        if (signature && (!input.TryReadUInt32LittleEndian(ref marker) || marker != 0x08074b50u)) return false;
        return input.TryReadUInt32LittleEndian(ref crc) && input.TryReadUInt32LittleEndian(ref compressed)
            && input.TryReadUInt32LittleEndian(ref uncompressed)
            && crc == record.Crc && compressed == record.Compressed && uncompressed == record.Uncompressed;
    }

    private static BinaryBufferReader LocalPayload(BinaryBufferReader image, nuint centralOffset,
        Record record, ref nuint budget)
    {
        BinaryBufferReader local = new();
        if (!image.TrySeek(0) || !image.TryReadSlice(centralOffset, ref local) || !local.TrySeek((nuint)record.Offset))
            throw new IOException("ZIP local header is outside the file-data region");
        uint signature = 0u; uint dateTime = 0u; uint crc = 0u; uint compressed = 0u; uint uncompressed = 0u;
        ushort version = 0; ushort flags = 0; ushort method = 0; ushort nameLength = 0; ushort extraLength = 0;
        if (!local.TryReadUInt32LittleEndian(ref signature) || signature != 0x04034b50u
            || !local.TryReadUInt16LittleEndian(ref version) || !local.TryReadUInt16LittleEndian(ref flags)
            || !local.TryReadUInt16LittleEndian(ref method) || !local.TryReadUInt32LittleEndian(ref dateTime)
            || !local.TryReadUInt32LittleEndian(ref crc) || !local.TryReadUInt32LittleEndian(ref compressed)
            || !local.TryReadUInt32LittleEndian(ref uncompressed) || !local.TryReadUInt16LittleEndian(ref nameLength)
            || !local.TryReadUInt16LittleEndian(ref extraLength)) throw new IOException("Invalid ZIP local header");
        if (version != record.Version || flags != record.Flags || method != record.Method || dateTime != record.DateTime)
            throw new IOException("ZIP local and central metadata do not match");
        if ((flags & (ushort)8) == 0)
        {
            if (crc != record.Crc || compressed != record.Compressed || uncompressed != record.Uncompressed)
                throw new IOException("ZIP local and central sizes or CRC do not match");
        }
        else if ((crc != 0u && crc != record.Crc) || (compressed != 0u && compressed != record.Compressed)
            || (uncompressed != 0u && uncompressed != record.Uncompressed))
            throw new IOException("ZIP descriptor placeholders do not match central metadata");
        Charge(30 + (nuint)nameLength + (nuint)extraLength, ref budget);
        BinaryBufferReader name = new(); BinaryBufferReader extra = new(); BinaryBufferReader payload = new();
        if (!local.TryReadSlice((nuint)nameLength, ref name) || !local.TryReadSlice((nuint)extraLength, ref extra)
            || !SameName(name, record.Name)) throw new IOException("ZIP local and central names do not match");
        CheckExtra(extra);
        if (!local.TryReadSlice((nuint)record.Compressed, ref payload)) throw new IOException("ZIP payload is truncated or overlaps its central directory");
        if ((flags & (ushort)8) != 0 && !DescriptorMatches(local, record, false) && !DescriptorMatches(local, record, true))
            throw new IOException("Invalid ZIP data descriptor");
        return payload;
    }
}
