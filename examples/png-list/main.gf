using Standard;
using Standard::Text;
using Standard::IO;
using Standard::Hashing;

bool TypeIs(readonly(char)* type, readonly(char)* expected) nothrow
{
    for (nuint i = 0; i < 4; i++) if (type[i] != expected[i]) return false;
    return true;
}

void PrintChunk(ITextWriter* output, ulong offset, readonly(char)* type, uint length)
{
    char[16] address;
    nuint written = 0;
    IntegerFormatting::TryFormatHex(offset, address, 16, ref written, 16, false);
    char[48] storage;
    BufferWriter row = new(storage, 48);
    row.Write(address, 16); row.Write(" "); row.Write(type, 4); row.Write(" ");
    length.Format(&row); row.Write("\n");
    output.Write(row.Data, row.Length);
}

void ListFile(readonly(char)* path, ITextWriter* output)
{
    File input = File::OpenRead(path);
    byte[8] expected = [(byte)137, (byte)80, (byte)78, (byte)71, (byte)13, (byte)10, (byte)26, (byte)10];
    char[8] signature;
    input.ReadExactly(signature, 8);
    for (nuint i = 0; i < 8; i++)
        if ((byte)signature[i] != expected[i]) throw new IOException("Invalid PNG signature");
    char[4] type;
    char[4096] buffer;
    ulong offset = 8ul;
    bool first = true;
    while (true)
    {
        uint length = FileBinary::ReadUInt32BigEndian(&input);
        if (length > 2147483647u) throw new IOException("PNG chunk length exceeds 31 bits");
        input.ReadExactly(type, 4);
        for (nuint i = 0; i < 4; i++)
            if (!Ascii::IsLetter(type[i])) throw new IOException("Invalid PNG chunk type");
        if (!Ascii::IsUpper(type[2])) throw new IOException("Reserved PNG chunk type bit is set");
        bool header = TypeIs(type, "IHDR");
        bool end = TypeIs(type, "IEND");
        if (first && (!header || length != 13u)) throw new IOException("PNG must start with a 13-byte IHDR");
        if (!first && header) throw new IOException("Duplicate PNG IHDR");
        if (end && length != 0u) throw new IOException("PNG IEND must be empty");
        ulong step = (ulong)length + 12ul;
        if (offset > (0ul - 1ul) - step) throw new IOException("PNG byte offset overflow");
        CRC32 crc = new();
        crc.Append(type, 4);
        uint remaining = length;
        while (remaining != 0u)
        {
            nuint amount = (nuint)remaining;
            if (amount > 4096) amount = 4096;
            input.ReadExactly(buffer, amount);
            crc.Append(buffer, amount);
            remaining -= (uint)amount;
        }
        uint stored = FileBinary::ReadUInt32BigEndian(&input);
        if (crc.GetValue() != stored) throw new IOException("PNG chunk CRC mismatch");
        if (end && input.ReadByte() != -1) throw new IOException("Trailing data after PNG IEND");
        PrintChunk(output, offset, type, length);
        if (end) break;
        first = false;
        offset += step;
    }
    input.Close();
}

int main(int argc, char** argv)
{
    if (argc != 2) { Console::Error.WriteLine("Usage: png-list <file>"); return 2; }
    try
    {
        char[4096] storage;
        BufferedWriter output = new(Console::Out, storage, 4096);
        ListFile(argv[1], &output);
        output.Flush();
        Console::Flush();
    }
    catch (ref IOException error)
    {
        Console::Error.Write("Unable to list PNG: ", 20);
        Console::Error.Write(argv[1], CString::Length(argv[1]));
        readonly(char)*? message = error.GetMessage();
        if (message != null) { Console::Error.Write(": ", 2); Console::Error.WriteLine(message!); }
        else Console::Error.WriteLine();
        return 1;
    }
    return 0;
}
