using Standard;
using Standard::Binary;
using Standard::Collections;
using Standard::IO;
using Standard::Text;

// Example-local file offsets and remaining file-backed bytes, never process addresses.
struct MappedBytes
{
    public ulong Offset;
    public ulong Extent;
    public MappedBytes(ulong offset, ulong extent) nothrow { Offset = offset; Extent = extent; }
}

nuint FileRange(BinaryBufferReader* input, ulong offset, ulong count)
{
    if (offset > (ulong)input.Length || count > (ulong)input.Length - offset)
        throw new IOException("Metadata range is outside the file");
    return (nuint)offset;
}

ushort U16(BinaryBufferReader* input, ulong offset)
{
    nuint start = FileRange(input, offset, 2ul); ushort value = (ushort)0;
    if (!input.TrySeek(start) || !input.TryReadUInt16LittleEndian(ref value))
        throw new IOException("Truncated metadata integer");
    return value;
}

uint U32(BinaryBufferReader* input, ulong offset)
{
    nuint start = FileRange(input, offset, 4ul); uint value = 0u;
    if (!input.TrySeek(start) || !input.TryReadUInt32LittleEndian(ref value))
        throw new IOException("Truncated metadata integer");
    return value;
}

ulong U64(BinaryBufferReader* input, ulong offset)
{
    nuint start = FileRange(input, offset, 8ul); ulong value = 0ul;
    if (!input.TrySeek(start) || !input.TryReadUInt64LittleEndian(ref value))
        throw new IOException("Truncated metadata integer");
    return value;
}

void PrintSize(nuint count)
{
    Console::Write("bytes: "); count.Format(Console::Out); Console::WriteLine();
}

void PrintMachine(ushort machine)
{
    readonly(char)* hex = "0123456789abcdef";
    char[4] digits;
    for (int i = 0; i < 4; i++) digits[(nuint)i] = hex[(nuint)((machine >> ((3 - i) * 4)) & 15)];
    Console::Write("machine: 0x"); Console::Write(digits, 4); Console::WriteLine();
}

// Only scan the mapped file-backed extent, not arbitrary bytes after a string table.
void PrintName(BinaryBufferReader* input, readonly(char)* label, ulong offset, ulong extent)
{
    nuint start = FileRange(input, offset, extent); nuint length = 0;
    if (extent == 0ul || !CString::TryGetLength((input.Data! + start).reinterpret<readonly(char)*>(), (nuint)extent, ref length))
        throw new IOException("Unterminated metadata name");
    readonly(char)* hex = "0123456789abcdef";
    Console::Write(label); Console::Write("\"");
    for (nuint i = 0; i < length; i++) {
        byte value = input.Data![start + i];
        if (value >= (byte)32 && value < (byte)127 && value != (byte)34 && value != (byte)92) {
            char character = (char)value; Console::Write(&character, 1);
        } else {
            char[4] escaped = ['\\', 'x', hex[(nuint)(value >> 4)], hex[(nuint)(value & (byte)15)]];
            Console::Write(escaped, 4);
        }
    }
    Console::WriteLine("\"");
}

bool RangesOverlap(ulong first, ulong firstSize, ulong second, ulong secondSize) nothrow
{
    if (firstSize == 0ul || secondSize == 0ul) return false;
    if (first <= second) return second - first < firstSize;
    return first - second < secondSize;
}

MappedBytes MapPe(BinaryBufferReader* input, ulong sections, ushort count, uint headers, uint rva, ulong needed)
{
    int matches = 0; MappedBytes result = new(0ul, 0ul);
    if (rva < headers && needed <= (ulong)headers - (ulong)rva) {
        FileRange(input, (ulong)rva, needed);
        result = new((ulong)rva, (ulong)headers - (ulong)rva); matches++;
    }
    for (nuint i = 0; i < (nuint)count; i++) {
        ulong entry = sections + (ulong)i * 40ul;
        uint address = U32(input, entry + 12ul); uint size = U32(input, entry + 16ul);
        uint raw = U32(input, entry + 20ul);
        if (rva < address) continue;
        ulong delta = (ulong)rva - (ulong)address;
        if (delta >= (ulong)size || needed > (ulong)size - delta) continue;
        ulong offset = (ulong)raw + delta;
        FileRange(input, offset, needed);
        result = new(offset, (ulong)size - delta); matches++;
    }
    if (matches != 1) throw new IOException("PE RVA has no unique file-backed mapping");
    return result;
}

void InspectPe(BinaryBufferReader* input)
{
    ulong pe = (ulong)U32(input, 60ul); FileRange(input, pe, 24ul);
    if (U32(input, pe) != 0x00004550u) throw new IOException("Invalid PE signature");
    ushort machine = U16(input, pe + 4ul); ushort count = U16(input, pe + 6ul);
    ushort optionalSize = U16(input, pe + 20ul); ulong optional = pe + 24ul;
    FileRange(input, optional, (ulong)optionalSize);
    if (optionalSize < (ushort)112 || U16(input, optional) != (ushort)0x20b)
        throw new IOException("Expected a PE32+ optional header");
    uint directories = U32(input, optional + 108ul);
    if ((ulong)directories > ((ulong)optionalSize - 112ul) / 8ul)
        throw new IOException("PE directory count exceeds the optional header");
    ulong sections = optional + (ulong)optionalSize;
    if (count == (ushort)0 || count > (ushort)96) throw new IOException("Unsupported PE section count");
    FileRange(input, sections, (ulong)count * 40ul);
    uint headers = U32(input, optional + 60ul); FileRange(input, 0ul, (ulong)headers);
    if ((ulong)headers < sections + (ulong)count * 40ul) throw new IOException("PE headers omit the section table");
    for (nuint i = 0; i < (nuint)count; i++) {
        ulong entry = sections + (ulong)i * 40ul; uint size = U32(input, entry + 16ul);
        uint raw = U32(input, entry + 20ul); uint address = U32(input, entry + 12ul);
        if (size != 0u) FileRange(input, (ulong)raw, (ulong)size);
        if ((ulong)size > 4294967296ul - (ulong)address) throw new IOException("PE section address range wraps");
        if (RangesOverlap((ulong)address, (ulong)size, 0ul, (ulong)headers))
            throw new IOException("Overlapping PE file-backed RVA ranges are unsupported");
        for (nuint j = 0; j < i; j++) {
            ulong previous = sections + (ulong)j * 40ul;
            if (RangesOverlap((ulong)address, (ulong)size, (ulong)U32(input, previous + 12ul), (ulong)U32(input, previous + 16ul)))
                throw new IOException("Overlapping PE file-backed RVA ranges are unsupported");
        }
    }
    Console::WriteLine("format: PE32+"); PrintSize(input.Length); PrintMachine(machine);
    uint clrRva = 0u; uint clrSize = 0u;
    if (directories > 14u) { clrRva = U32(input, optional + 224ul); clrSize = U32(input, optional + 228ul); }
    if (clrRva == 0u && clrSize == 0u) Console::WriteLine("clr-directory: absent");
    else {
        if (clrRva == 0u || clrSize == 0u) throw new IOException("Incomplete PE CLR directory");
        MapPe(input, sections, count, headers, clrRva, (ulong)clrSize);
        Console::WriteLine("clr-directory: present");
    }
    uint importRva = 0u; uint importSize = 0u;
    if (directories > 1u) { importRva = U32(input, optional + 120ul); importSize = U32(input, optional + 124ul); }
    if (importRva == 0u && importSize == 0u) { Console::WriteLine("import: none"); return; }
    if (importRva == 0u || importSize < 20u) throw new IOException("Incomplete PE import directory");
    MappedBytes imports = MapPe(input, sections, count, headers, importRva, (ulong)importSize);
    ulong at = 0ul; nuint names = 0;
    while ((ulong)importSize - at >= 20ul) {
        ulong entry = imports.Offset + at;
        uint lookup = U32(input, entry); uint stamp = U32(input, entry + 4ul);
        uint chain = U32(input, entry + 8ul); uint name = U32(input, entry + 12ul); uint address = U32(input, entry + 16ul);
        if (lookup == 0u && stamp == 0u && chain == 0u && name == 0u && address == 0u) {
            if (names == 0) Console::WriteLine("import: none");
            return;
        }
        if (name == 0u) throw new IOException("PE import descriptor has no name");
        MappedBytes text = MapPe(input, sections, count, headers, name, 1ul);
        PrintName(input, "import: ", text.Offset, text.Extent); names++; at += 20ul;
    }
    throw new IOException("Unterminated PE import directory");
}

MappedBytes MapElf(BinaryBufferReader* input, ulong table, ushort count, ulong address, ulong needed)
{
    int matches = 0; MappedBytes result = new(0ul, 0ul);
    for (nuint i = 0; i < (nuint)count; i++) {
        ulong entry = table + (ulong)i * 56ul;
        if (U32(input, entry) != 1u) continue;
        ulong virtualAddress = U64(input, entry + 16ul); ulong size = U64(input, entry + 32ul);
        if (address < virtualAddress) continue;
        ulong delta = address - virtualAddress;
        if (delta >= size || needed > size - delta) continue;
        ulong offset = U64(input, entry + 8ul) + delta;
        FileRange(input, offset, needed);
        result = new(offset, size - delta); matches++;
    }
    if (matches != 1) throw new IOException("ELF address has no unique file-backed mapping");
    return result;
}

void InspectElf(BinaryBufferReader* input)
{
    FileRange(input, 0ul, 64ul);
    if (input.Data![4] != (byte)2 || input.Data![5] != (byte)1 || input.Data![6] != (byte)1 || U32(input, 20ul) != 1u)
        throw new IOException("Expected ELF64 little-endian version 1");
    ushort kind = U16(input, 16ul); ushort machine = U16(input, 18ul);
    if (kind != (ushort)2 && kind != (ushort)3) throw new IOException("Expected an ELF executable or shared object");
    if (U16(input, 52ul) != (ushort)64) throw new IOException("Unsupported ELF header size");
    ulong table = U64(input, 32ul); ushort count = U16(input, 56ul);
    if (count == (ushort)65535) throw new IOException("Extended ELF program counts are unsupported");
    if (count > (ushort)256) throw new IOException("ELF program count exceeds the 256-entry example limit");
    if (count != (ushort)0 && U16(input, 54ul) != (ushort)56) throw new IOException("Unsupported ELF program entry size");
    FileRange(input, table, (ulong)count * 56ul);
    bool hasInterpreter = false; bool hasDynamic = false;
    ulong interpreter = 0ul; ulong interpreterSize = 0ul; ulong dynamic = 0ul; ulong dynamicSize = 0ul;
    for (nuint i = 0; i < (nuint)count; i++) {
        ulong entry = table + (ulong)i * 56ul; uint type = U32(input, entry);
        if (type == 0u) continue;
        ulong offset = U64(input, entry + 8ul); ulong size = U64(input, entry + 32ul);
        FileRange(input, offset, size);
        if (type == 1u) {
            ulong address = U64(input, entry + 16ul);
            if (size > U64(input, entry + 40ul) || size > (0ul - 1ul) - address)
                throw new IOException("Invalid ELF load segment range");
            for (nuint j = 0; j < i; j++) {
                ulong previous = table + (ulong)j * 56ul;
                if (U32(input, previous) == 1u && RangesOverlap(address, size, U64(input, previous + 16ul), U64(input, previous + 32ul)))
                    throw new IOException("Overlapping ELF file-backed VA ranges are unsupported");
            }
        }
        if (type == 3u) {
            if (hasInterpreter) throw new IOException("Multiple ELF interpreters are unsupported");
            hasInterpreter = true; interpreter = offset; interpreterSize = size;
        }
        if (type == 2u) {
            if (hasDynamic) throw new IOException("Multiple ELF dynamic segments are unsupported");
            hasDynamic = true; dynamic = offset; dynamicSize = size;
        }
    }
    Console::WriteLine("format: ELF64LE"); PrintSize(input.Length); PrintMachine(machine);
    if (hasInterpreter) PrintName(input, "interpreter: ", interpreter, interpreterSize);
    else Console::WriteLine("interpreter: absent");
    if (!hasDynamic) { Console::WriteLine("needed: none"); return; }
    if (dynamicSize % 16ul != 0ul) throw new IOException("Incomplete ELF dynamic entry");
    bool ended = false; bool hasStrings = false; bool hasStringSize = false;
    ulong strings = 0ul; ulong stringSize = 0ul; ulong needed = 0ul; ulong used = 0ul;
    for (ulong at = 0ul; at < dynamicSize; at += 16ul) {
        ulong tag = U64(input, dynamic + at); ulong value = U64(input, dynamic + at + 8ul);
        if (tag == 0ul) { ended = true; used = at; break; }
        if (tag == 1ul) needed++;
        if (tag == 5ul) {
            if (hasStrings) throw new IOException("Duplicate ELF string table address");
            hasStrings = true; strings = value;
        }
        if (tag == 10ul) {
            if (hasStringSize) throw new IOException("Duplicate ELF string table size");
            hasStringSize = true; stringSize = value;
        }
    }
    if (!ended) throw new IOException("Unterminated ELF dynamic segment");
    if (needed == 0ul) { Console::WriteLine("needed: none"); return; }
    if (!hasStrings || !hasStringSize || stringSize == 0ul) throw new IOException("Missing ELF dependency string table");
    MappedBytes text = MapElf(input, table, count, strings, stringSize);
    for (ulong at = 0ul; at < used; at += 16ul) {
        if (U64(input, dynamic + at) != 1ul) continue;
        ulong name = U64(input, dynamic + at + 8ul);
        if (name >= stringSize) throw new IOException("ELF dependency name exceeds its string table");
        PrintName(input, "needed: ", text.Offset + name, stringSize - name);
    }
}

void InspectFile(readonly(char)* path)
{
    Vector<byte> image = FileBytes::ReadAll(path, 67108864);
    if (image.Count < 4) throw new IOException("Unrecognized or truncated binary signature");
    // No owner growth or mutation after this point: all metadata views borrow image.
    BinaryBufferReader reader = new(image.AsSpan().Data!, image.Count);
    if (image[0] == (byte)'M' && image[1] == (byte)'Z') InspectPe(&reader);
    else if (image[0] == (byte)127 && image[1] == (byte)'E' && image[2] == (byte)'L' && image[3] == (byte)'F') InspectElf(&reader);
    else throw new IOException("Unsupported binary format");
    Console::Flush();
}

int main(int argc, char** argv)
{
    if (argc != 2) {
        try { Console::Error.WriteLine("Usage: binary-info <file>"); Console::FlushError(); }
        catch (ref Exception ignored) { }
        return 2;
    }
    try { InspectFile(argv[1]); }
    catch (ref Exception error) {
        try {
            Console::Error.Write("binary-info: ", 13);
            readonly(char)*? message = error.GetMessage();
            if (message != null) Console::Error.WriteLine(message!);
            else Console::Error.WriteLine("Inspection failed");
            Console::FlushError();
        }
        catch (ref Exception ignored) { }
        return 1;
    }
    return 0;
}
