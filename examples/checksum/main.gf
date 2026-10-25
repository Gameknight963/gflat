using std;

// Format the numeric checksum in a fixed byte order, independent of this CPU.
void WriteChecksum(uint value)
{
    byte[4] storage;
    byte* bytes = storage;
    BinaryPrimitives::TryWriteUInt32BigEndian(bytes, 4, value);
    Console::Out.WriteHex((readonly(char)*)bytes, 4, false);
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

uint ReadCRC32C(readonly(char)* path)
{
    File input = File::OpenRead(path);
    CRC32C crc = new();
    char[4096] buffer;
    while (true)
    {
        nuint count = input.Read(buffer, 4096);
        if (count == 0) break;
        crc.Append(buffer, count);
    }
    input.Close();
    return crc.GetValue();
}

int main(int argc, char** argv)
{
    bool sha256 = false;
    bool crc32c = false;
    int firstPath = 1;
    while (firstPath < argc)
    {
        readonly(char)* argument = argv[firstPath];
        if (CString::Equals(argument, "--")) { firstPath++; break; }
        if (CString::Equals(argument, "--sha256") && !sha256 && !crc32c)
        { sha256 = true; firstPath++; continue; }
        if (CString::Equals(argument, "--crc32c") && !sha256 && !crc32c)
        { crc32c = true; firstPath++; continue; }
        if (argument[0] == '-')
        {
            Console::Error.WriteLine("Usage: checksum [--sha256 | --crc32c] [--] <file> [more files...]");
            return 2;
        }
        break;
    }
    if (firstPath >= argc)
    {
        Console::Error.WriteLine("Usage: checksum [--sha256 | --crc32c] [--] <file> [more files...]");
        return 2;
    }
    int status = 0;
    try
    {
        for (int i = firstPath; i < argc; i++)
        {
            uint crc = 0;
            uint adler = 0;
            byte[32] digest;
            try
            {
                if (sha256) digest = FileHash::SHA256(argv[i]);
                else if (crc32c) crc = ReadCRC32C(argv[i]);
                else ReadChecksums(argv[i], ref crc, ref adler);
            }
            catch (ref IOException error)
            {
                Console::Error.Write("Unable to read: ", 16);
                Console::Error.WriteLine(argv[i]);
                status = 1;
                continue;
            }
            if (sha256)
            {
                Console::Out.WriteHex((readonly(char)*)(byte*)digest, 32, false);
            }
            else if (crc32c) WriteChecksum(crc);
            else
            {
                WriteChecksum(crc);
                Console::Write(" ");
                WriteChecksum(adler);
            }
            Console::Write("  ");
            Console::WriteLine(argv[i]);
        }
        // Buffered writes can fail only when the pending bytes are flushed.
        Console::Flush();
    }
    catch (ref IOException error)
    {
        Console::Error.WriteLine("Unable to write checksums");
        return 1;
    }
    return status;
}
