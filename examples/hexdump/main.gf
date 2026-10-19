using std;

// One stack-backed output row, including a full-width byte offset.
void PrintRow(ulong offset, readonly(char)* bytes, nuint count)
{
    byte[8] address;
    char[16] addressText;
    char[32] byteText;
    nuint written = 0;
    BinaryPrimitives::TryWriteUInt64BigEndian(address, 8, offset);
    Hex::TryEncode((readonly(char)*)address, 8, addressText, 16, ref written, false);
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
            if (!std::Ascii::IsPrintable(display)) display = '.';
        }
        output.Write(&display, 1);
    }
    output.Write("|\n");
    Console::Write(output.Data, output.Length);
}

void DumpFile(readonly(char)* path)
{
    File input = File::OpenRead(path);
    char[4096] buffer;
    ulong offset = 0ul;
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

void Error(readonly(char)* message)
{
    nuint length = 0;
    while (message[length] != '\0') length++;
    Console::Error.Write(message, length);
}

int main(int argc, char** argv)
{
    if (argc != 2) { Error("Usage: hexdump <file>\n"); return 2; }
    try { DumpFile(argv[1]); }
    catch (IOException* error)
    {
        Error("Unable to dump: "); Error(argv[1]); Error("\n");
        return 1;
    }
    return 0;
}
