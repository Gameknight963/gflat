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

void ReadChecksums(readonly(char)* path, ref uint crcValue, ref uint adlerValue)
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
    crcValue = crc.GetValue();
    adlerValue = adler.GetValue();
}

int main(int argc, char** argv)
{
    if (argc < 2)
    {
        Console::Error.WriteLine("Usage: checksum <file> [more files...]");
        return 2;
    }
    int status = 0;
    try
    {
        for (int i = 1; i < argc; i++)
        {
            uint crc = 0;
            uint adler = 0;
            try { ReadChecksums(argv[i], ref crc, ref adler); }
            catch (IOException* error)
            {
                Console::Error.Write("Unable to read: ", 16);
                Console::Error.WriteLine(argv[i]);
                status = 1;
                continue;
            }
            WriteChecksum(crc);
            Console::Write(" ");
            WriteChecksum(adler);
            Console::Write("  ");
            Console::WriteLine(argv[i]);
        }
        // Buffered writes can fail only when the pending bytes are flushed.
        Console::Flush();
    }
    catch (IOException* error)
    {
        Console::Error.WriteLine("Unable to write checksums");
        return 1;
    }
    return status;
}
