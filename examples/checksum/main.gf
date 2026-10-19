using std;

// Format the numeric checksum in a fixed byte order, independent of this CPU.
void WriteChecksum(uint value)
{
    byte[4] storage;
    byte* bytes = storage;
    char[8] hex;
    nuint written = 0;
    BinaryPrimitives::TryWriteUInt32BigEndian(bytes, 4, value);
    Hex::TryEncode((readonly(char)*)bytes, 4, hex, 8, ref written, false);
    Console::Write(hex, written);
}

void PrintFile(readonly(char)* path)
{
    File input = File::OpenRead(path);
    CRC32 crc = new();
    Adler32 adler = new();
    char[4096] buffer;
    while (true)
    {
        nuint count = input.Read(buffer, 4096);
        if (count == 0) break;
        crc.Append(buffer, count);
        adler.Append(buffer, count);
    }
    input.Close();
    WriteChecksum(crc.GetValue());
    Console::Write(" ");
    WriteChecksum(adler.GetValue());
    Console::Write("  ");
    Console::WriteLine(path);
}

int main(int argc, char** argv)
{
    if (argc < 2)
    {
        Console::WriteLine("Usage: checksum <file> [more files...]");
        return 2;
    }
    int status = 0;
    for (int i = 1; i < argc; i++)
    {
        try { PrintFile(argv[i]); }
        catch (IOException* error)
        {
            Console::Write("Unable to read: ");
            Console::WriteLine(argv[i]);
            status = 1;
        }
    }
    return status;
}
