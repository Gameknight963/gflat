using std;
using std::Text;
using std::IO;
using std::Encoding;

// Preserve all four identifier bytes without writing controls to the terminal.
void WriteFourCC(BufferWriter* output, readonly(char)* id)
{
    output.Write("\"");
    for (nuint i = 0; i < 4; i++)
    {
        char value = id[i];
        if (Ascii::IsPrintable(value) && value != '\"' && value != '\\')
            output.Write(&value, 1);
        else
        {
            char[2] encoded;
            nuint written = 0;
            Hex::TryEncode(&value, 1, encoded, 2, ref written, false);
            output.Write("\\x");
            output.Write(encoded, 2);
        }
    }
    output.Write("\"");
}

// Header offsets are hexadecimal; sizes are decimal and exclude header/padding.
void PrintEntry(ulong offset, readonly(char)* id, uint size, readonly(char)*? form)
{
    char[16] address;
    nuint written = 0;
    IntegerFormatting::TryFormatHex(offset, address, 16, ref written, 16, false);
    char[96] storage;
    BufferWriter row = new(storage, 96);
    row.Write(address, 16); row.Write(" ");
    WriteFourCC(&row, id); row.Write(" "); size.Format(&row);
    if (form != null) { row.Write(" "); WriteFourCC(&row, form!); }
    row.Write("\n");
    Console::Write(row.Data, row.Length);
}

void ListFile(readonly(char)* path)
{
    File input = File::OpenRead(path);
    char[4] id;
    input.ReadExactly(id, 4);
    if (id[0] != 'R' || id[1] != 'I' || id[2] != 'F' || id[3] != 'F')
        throw new IOException("Expected a little-endian RIFF root");
    uint rootSize = FileBinary::ReadUInt32LittleEndian(&input);
    if (rootSize < 4u) throw new IOException("RIFF root has no complete form type");
    char[4] form;
    input.ReadExactly(form, 4);
    PrintEntry(0ul, id, rootSize, form);
    ulong remaining = (ulong)rootSize - 4ul;
    ulong offset = 12ul;
    while (remaining != 0ul)
    {
        if (remaining < 8ul) throw new IOException("Incomplete RIFF chunk header");
        input.ReadExactly(id, 4);
        uint size = FileBinary::ReadUInt32LittleEndian(&input);
        remaining -= 8ul;
        ulong padded = (ulong)size + ((ulong)size & 1ul);
        if (padded > remaining) throw new IOException("RIFF chunk exceeds root size");
        input.SkipExactly((nuint)size);
        if ((size & 1u) != 0u) input.SkipExactly(1);
        PrintEntry(offset, id, size, null);
        remaining -= padded;
        offset += 8ul + padded;
    }
    if (input.ReadByte() != -1) throw new IOException("Trailing data after RIFF root");
    input.Close();
    Console::Flush();
}

int main(int argc, char** argv)
{
    if (argc != 2) { Console::Error.WriteLine("Usage: riff-list <file>"); return 2; }
    try { ListFile(argv[1]); }
    catch (ref IOException error)
    {
        Console::Error.Write("Unable to list RIFF: ", 21);
        Console::Error.WriteLine(argv[1]);
        return 1;
    }
    return 0;
}
