using Standard::Binary;
using Standard::Collections;
using Standard::Hashing;
using Standard::IO;
using Standard::Memory;
using Standard::Text;

namespace Standard::Compression;

// Borrows a new archive destination. Call Complete to write the central directory.
public struct ZipArchiveWriter
{
    private ITextWriter* output;
    private Vector<Record> entries;
    private ulong position;
    private ulong centralLength;
    private bool completed;
    private bool failed;

    public ZipArchiveWriter(ITextWriter* destination) nothrow
    {
        output = destination;
        entries = new();
        position = 0ul;
        centralLength = 0ul;
        completed = false;
        failed = false;
    }

    public nuint Count { immutable get nothrow => entries.Count; }
    public bool IsCompleted { immutable get nothrow => completed; }

    public void AddEntry(readonly(char)* name, readonly(byte)* data, nuint count)
        => this.AddEntry(new Span<readonly(char)>(name, CString::Length(name)), new Span<readonly(byte)>(data, count));

    public void AddEntry(Span<readonly(char)> name, Span<readonly(byte)> data)
    {
        this.CheckUsable();
        if (completed) throw new IOException("ZIP writer is already complete");
        if (name.IsEmpty || name.Length > 65535) throw new IOException("ZIP entry name must contain 1 to 65535 UTF-8 bytes");
        if (!Utf8::IsValid(name.Data!, name.Length)) throw new IOException("Invalid UTF-8 ZIP entry name");
        if (entries.Count >= 65534 || (ulong)data.Length >= 0xfffffffful)
            throw new IOException("ZIP32 entry size or count limit exceeded");
        ulong nextPosition = position + 30ul + (ulong)name.Length + (ulong)data.Length;
        ulong nextCentral = centralLength + 46ul + (ulong)name.Length;
        if (nextPosition + nextCentral + 22ul >= 0xfffffffful)
            throw new IOException("ZIP32 archive size limit exceeded");

        CRC32 checksum = new();
        if (!data.IsEmpty) checksum.Append(data.Data!.reinterpret<readonly(char)*>(), data.Length);
        String ownedName = String::From(name.Data!, name.Length);
        Record record = new(move ownedName, checksum.GetValue(), (uint)data.Length, (uint)position);
        if (entries.Count == entries.Capacity)
        {
            nuint capacity = 4;
            if (entries.Capacity != 0) capacity = entries.Capacity * 2;
            if (capacity > 65534) capacity = 65534;
            entries.Reserve(capacity);
        }

        byte[30] header;
        byte* bytes = header;
        BinaryPrimitives::TryWriteUInt32LittleEndian(bytes, 30, 0x04034b50u);
        BinaryPrimitives::TryWriteUInt16LittleEndian(bytes + 4, 26, 20);
        BinaryPrimitives::TryWriteUInt16LittleEndian(bytes + 6, 24, 2048);
        BinaryPrimitives::TryWriteUInt16LittleEndian(bytes + 8, 22, 0);
        BinaryPrimitives::TryWriteUInt16LittleEndian(bytes + 10, 20, 0);
        BinaryPrimitives::TryWriteUInt16LittleEndian(bytes + 12, 18, 33);
        BinaryPrimitives::TryWriteUInt32LittleEndian(bytes + 14, 16, record.Crc);
        BinaryPrimitives::TryWriteUInt32LittleEndian(bytes + 18, 12, record.Length);
        BinaryPrimitives::TryWriteUInt32LittleEndian(bytes + 22, 8, record.Length);
        BinaryPrimitives::TryWriteUInt16LittleEndian(bytes + 26, 4, (ushort)record.Name.Length);
        BinaryPrimitives::TryWriteUInt16LittleEndian(bytes + 28, 2, 0);

        failed = true;
        output.Write(bytes.reinterpret<readonly(char)*>(), 30);
        output.Write(record.Name.Data, record.Name.Length);
        if (!data.IsEmpty) output.Write(data.Data!.reinterpret<readonly(char)*>(), data.Length);
        entries.Add(move record);
        position = nextPosition;
        centralLength = nextCentral;
        failed = false;
    }

    public void Complete()
    {
        this.CheckUsable();
        if (completed) return;
        failed = true;
        byte[46] header;
        byte* bytes = header;
        for (nuint index = 0; index < entries.Count; index++)
        {
            Record* record = &entries[index];
            BinaryPrimitives::TryWriteUInt32LittleEndian(bytes, 46, 0x02014b50u);
            BinaryPrimitives::TryWriteUInt16LittleEndian(bytes + 4, 42, 20);
            BinaryPrimitives::TryWriteUInt16LittleEndian(bytes + 6, 40, 20);
            BinaryPrimitives::TryWriteUInt16LittleEndian(bytes + 8, 38, 2048);
            BinaryPrimitives::TryWriteUInt16LittleEndian(bytes + 10, 36, 0);
            BinaryPrimitives::TryWriteUInt16LittleEndian(bytes + 12, 34, 0);
            BinaryPrimitives::TryWriteUInt16LittleEndian(bytes + 14, 32, 33);
            BinaryPrimitives::TryWriteUInt32LittleEndian(bytes + 16, 30, record.Crc);
            BinaryPrimitives::TryWriteUInt32LittleEndian(bytes + 20, 26, record.Length);
            BinaryPrimitives::TryWriteUInt32LittleEndian(bytes + 24, 22, record.Length);
            BinaryPrimitives::TryWriteUInt16LittleEndian(bytes + 28, 18, (ushort)record.Name.Length);
            BinaryPrimitives::TryWriteUInt16LittleEndian(bytes + 30, 16, 0);
            BinaryPrimitives::TryWriteUInt16LittleEndian(bytes + 32, 14, 0);
            BinaryPrimitives::TryWriteUInt16LittleEndian(bytes + 34, 12, 0);
            BinaryPrimitives::TryWriteUInt16LittleEndian(bytes + 36, 10, 0);
            uint attributes = 0u;
            if (record.Name.Data[record.Name.Length - 1] == '/') attributes = 16u;
            BinaryPrimitives::TryWriteUInt32LittleEndian(bytes + 38, 8, attributes);
            BinaryPrimitives::TryWriteUInt32LittleEndian(bytes + 42, 4, record.Offset);
            output.Write(bytes.reinterpret<readonly(char)*>(), 46);
            output.Write(record.Name.Data, record.Name.Length);
        }
        BinaryPrimitives::TryWriteUInt32LittleEndian(bytes, 46, 0x06054b50u);
        BinaryPrimitives::TryWriteUInt16LittleEndian(bytes + 4, 42, 0);
        BinaryPrimitives::TryWriteUInt16LittleEndian(bytes + 6, 40, 0);
        BinaryPrimitives::TryWriteUInt16LittleEndian(bytes + 8, 38, (ushort)entries.Count);
        BinaryPrimitives::TryWriteUInt16LittleEndian(bytes + 10, 36, (ushort)entries.Count);
        BinaryPrimitives::TryWriteUInt32LittleEndian(bytes + 12, 34, (uint)centralLength);
        BinaryPrimitives::TryWriteUInt32LittleEndian(bytes + 16, 30, (uint)position);
        BinaryPrimitives::TryWriteUInt16LittleEndian(bytes + 20, 26, 0);
        output.Write(bytes.reinterpret<readonly(char)*>(), 22);
        completed = true;
        failed = false;
    }

    private immutable void CheckUsable()
    {
        if (failed) throw new IOException("ZIP writer cannot continue after output failure");
    }

    private struct Record
    {
        public String Name;
        public uint Crc;
        public uint Length;
        public uint Offset;
        public Record(String name, uint crc, uint length, uint offset)
        {
            Name = move name;
            Crc = crc;
            Length = length;
            Offset = offset;
        }
    }
}
