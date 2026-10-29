using std;
using std::Binary;
using std::Collections;
using std::IO;
using std::Text;

// Example-local metadata. Name borrows the stable complete input image.
struct Entry
{
    public uint Compressed;
    public uint Uncompressed;
    public ushort Method;
    public BinaryBufferReader Name = new();
}

bool TryReadEntry(ref BinaryBufferReader directory, ref Entry entry) nothrow
{
    entry = new();
    uint signature = 0u; uint localOffset = 0u;
    ushort nameLength = 0; ushort extraLength = 0; ushort commentLength = 0; ushort disk = 0;
    if (!directory.TryReadUInt32LittleEndian(ref signature) || signature != 0x02014b50u
        || !directory.TrySkip(6) || !directory.TryReadUInt16LittleEndian(ref entry.Method)
        || !directory.TrySkip(8) || !directory.TryReadUInt32LittleEndian(ref entry.Compressed)
        || !directory.TryReadUInt32LittleEndian(ref entry.Uncompressed)
        || !directory.TryReadUInt16LittleEndian(ref nameLength)
        || !directory.TryReadUInt16LittleEndian(ref extraLength)
        || !directory.TryReadUInt16LittleEndian(ref commentLength)
        || !directory.TryReadUInt16LittleEndian(ref disk) || !directory.TrySkip(6)
        || !directory.TryReadUInt32LittleEndian(ref localOffset)) return false;
    if (disk != 0 || entry.Compressed == 0xffffffffu || entry.Uncompressed == 0xffffffffu
        || localOffset == 0xffffffffu) return false;
    return directory.TryReadSlice((nuint)nameLength, ref entry.Name)
        && directory.TrySkip((nuint)extraLength) && directory.TrySkip((nuint)commentLength);
}

bool TryDirectory(BinaryBufferReader image, nuint at, ref BinaryBufferReader directory,
    ref ushort entries) nothrow
{
    directory = new(); entries = 0;
    uint signature = 0u; uint size = 0u; uint offset = 0u;
    ushort disk = 0; ushort startDisk = 0; ushort onDisk = 0; ushort commentLength = 0;
    if (!image.TrySeek(at) || !image.TryReadUInt32LittleEndian(ref signature) || signature != 0x06054b50u
        || !image.TryReadUInt16LittleEndian(ref disk) || !image.TryReadUInt16LittleEndian(ref startDisk)
        || !image.TryReadUInt16LittleEndian(ref onDisk) || !image.TryReadUInt16LittleEndian(ref entries)
        || !image.TryReadUInt32LittleEndian(ref size) || !image.TryReadUInt32LittleEndian(ref offset)
        || !image.TryReadUInt16LittleEndian(ref commentLength)) return false;
    if (disk != 0 || startDisk != 0 || onDisk != entries || entries == (ushort)65535
        || size == 0xffffffffu || offset == 0xffffffffu || image.Remaining != (nuint)commentLength) return false;
    if ((nuint)offset > at || (nuint)size != at - (nuint)offset) return false;
    // Every entry needs its 46-byte fixed header, before any variable fields.
    if ((nuint)entries > (nuint)size / 46) return false;
    if (!image.TrySeek((nuint)offset) || !image.TryReadSlice((nuint)size, ref directory)) return false;
    BinaryBufferReader walk = directory;
    Entry entry = new();
    for (nuint i = 0; i < (nuint)entries; i++)
        if (!TryReadEntry(ref walk, ref entry)) return false;
    return walk.Remaining == 0;
}

bool FindDirectory(BinaryBufferReader image, ref BinaryBufferReader directory, ref ushort entries) nothrow
{
    directory = new(); entries = 0;
    if (image.Length < 22) return false;
    nuint at = image.Length - 22;
    nuint lower = 0;
    if (at > 65535) lower = at - 65535;
    while (true) {
        // Validate the whole candidate, not merely a signature inside an archive comment.
        if (TryDirectory(image, at, ref directory, ref entries)) return true;
        if (at == lower) return false;
        at--;
    }
}

void PrintName(BinaryBufferReader name)
{
    Console::Write("\"");
    readonly(char)* hex = "0123456789ABCDEF";
    for (nuint i = 0; i < name.Length; i++) {
        byte value = name.Data![i];
        if (value >= (byte)32 && value <= (byte)126 && value != (byte)'"' && value != (byte)'\\') {
            char display = (char)value;
            Console::Out.Write(&display, 1);
        }
        else {
            char[4] escaped;
            escaped[0] = '\\'; escaped[1] = 'x';
            escaped[2] = hex[value >> 4]; escaped[3] = hex[value & 15];
            Console::Out.Write(escaped, 4);
        }
    }
    Console::WriteLine("\"");
}

void ListFile(readonly(char)* path)
{
    Vector<byte> image = FileBytes::ReadAll(path, 134217728);
    // No mutation or growth after this point; both passes borrow this owner.
    BinaryBufferReader input = new();
    if (image.Count != 0) input = new(image.AsSpan().Data!, image.Count);
    BinaryBufferReader directory = new(); ushort entries = 0;
    if (!FindDirectory(input, ref directory, ref entries))
        throw new IOException("Unsupported or invalid ZIP32 central directory");
    // Validation is complete before any stdout output. No Entry collection is allocated.
    Console::WriteLine("compressed uncompressed method name");
    Entry entry = new();
    for (nuint i = 0; i < (nuint)entries; i++) {
        TryReadEntry(ref directory, ref entry);
        entry.Compressed.Format(Console::Out); Console::Write(" ");
        entry.Uncompressed.Format(Console::Out); Console::Write(" ");
        entry.Method.Format(Console::Out); Console::Write(" ");
        PrintName(entry.Name);
    }
    Console::Flush();
}

int main(int argc, char** argv)
{
    if (argc != 2) {
        try { Console::Error.WriteLine("Usage: zip-list <file>"); Console::FlushError(); }
        catch (ref Exception ignored) { }
        return 2;
    }
    try { ListFile(argv[1]); }
    catch (ref Exception error) {
        try {
            Console::Error.Write("zip-list: ", 10);
            Console::Error.WriteLine(error.GetMessage()); Console::FlushError();
        }
        catch (ref Exception ignored) { }
        return 1;
    }
    return 0;
}
