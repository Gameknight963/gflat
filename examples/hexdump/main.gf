using Standard::Text;
using Standard::IO;
using Standard::Encoding;
using Standard;

// One stack-backed output row, including a full-width byte offset.
void PrintRow(ulong offset, readonly(char)* bytes, nuint count)
{
    char[16] addressText;
    char[32] byteText;
    nuint written = 0;
    IntegerFormatting::TryFormatHex(offset, addressText, 16, ref written, 16, false);
    Hex::TryEncode(bytes, count, byteText, 32, ref written, false);
    char[87] row;
    BufferWriter output = new(row, 87);
    output.Write(addressText, 16);
    output.Write("  ");
    for (nuint column = 0; column < 16; column++)
    {
        if (column < count) { output.Write(&byteText[column * 2], 2); output.Write(" "); }
        else output.Write("   ");
    }
    output.Write(" |");
    for (nuint column = 0; column < 16; column++)
    {
        char display = ' ';
        if (column < count) {
            display = bytes[column];
            if (!Standard::Text::Ascii::IsPrintable(display)) display = '.';
        }
        output.Write(&display, 1);
    }
    output.Write("|\n");
    Console::Write(output.Data, output.Length);
}

void DumpFile(readonly(char)* path, nuint skip)
{
    File input = File::OpenRead(path);
    input.SkipExactly(skip);
    char[4096] buffer;
    ulong offset = (ulong)skip;
    while (true)
    {
        nuint count = input.Read(buffer, 4096);
        if (count == 0) break;
        for (nuint start = 0; start < count; start += 16)
        {
            nuint length = count - start;
            if (length > 16) length = 16;
            PrintRow(offset, (readonly(char)*)buffer + start, length);
            offset += (ulong)length;
        }
    }
    input.Close();
    Console::Flush();
}

int main(int argc, char** argv)
{
    nuint skip = 0;
    if (argc != 2 && (argc != 4 || !CString::Equals(argv[1], "--skip") || !TryParseNUInt(argv[2], &skip)))
    {
        Console::Error.WriteLine("Usage: hexdump [--skip <count>] <file>");
        return 2;
    }
    readonly(char)* path = argv[1];
    if (argc == 4) path = argv[3];
    try { DumpFile(path, skip); }
    catch (ref IOException error)
    {
        Console::Error.Write("Unable to dump: ", 16);
        Console::Error.WriteLine(path);
        return 1;
    }
    return 0;
}
