using Standard;
using Standard::IO;
using Standard::Text;
using Standard::Memory;
using Standard::Collections;
using Standard::Encoding;

void WriteToken(BufferWriter* output, readonly(char)* bytes, nuint count, nuint at)
{
    if (at == count) { output.Write("EOF"); return; }
    char[2] text;
    nuint written = 0;
    Hex::TryEncode(bytes + at, 1, text, 2, ref written, false);
    output.Write(text, 2);
}

void PrintDifference(ulong offset, readonly(char)* left, nuint leftCount,
    readonly(char)* right, nuint rightCount, nuint at)
{
    char[16] address;
    nuint written = 0;
    IntegerFormatting::TryFormatHex(offset, address, 16, ref written, 16, false);
    char[64] storage;
    BufferWriter row = new(storage, 64);
    if (at < leftCount && at < rightCount) row.Write("byte ");
    else row.Write("length ");
    row.Write(address, 16); row.Write(" ");
    WriteToken(&row, left, leftCount, at); row.Write(" ");
    WriteToken(&row, right, rightCount, at); row.Write("\n");
    Console::Write(row.Data, row.Length);
    Console::Flush();
}

int CompareFiles(readonly(char)* leftPath, readonly(char)* rightPath)
{
    File left = File::OpenRead(leftPath);
    File right = File::OpenRead(rightPath);
    char[4096] leftBuffer;
    char[4096] rightBuffer;
    ulong offset = 0ul;
    while (true)
    {
        nuint leftCount = left.Read(leftBuffer, 4096);
        nuint rightCount = right.Read(rightBuffer, 4096);
        Span<immutable(byte)> first = new(((char*)leftBuffer).reinterpret<immutable(byte)*>(), leftCount);
        Span<immutable(byte)> second = new(((char*)rightBuffer).reinterpret<immutable(byte)*>(), rightCount);
        nuint common = Standard::Collections::CommonPrefixLength<byte>(first, second);
        if ((ulong)common > 18446744073709551615ul - offset)
            throw new IOException("File comparison offset overflow");
        if ((common < leftCount && common < rightCount) || leftCount != rightCount)
        {
            left.Close(); right.Close();
            PrintDifference(offset + (ulong)common, leftBuffer, leftCount, rightBuffer, rightCount, common);
            return 1;
        }
        if (leftCount == 0) { left.Close(); right.Close(); return 0; }
        offset += (ulong)common;
    }
}

int main(int argc, char** argv)
{
    if (argc != 3)
    {
        try { Console::Error.WriteLine("Usage: file-compare <left> <right>"); Console::FlushError(); }
        catch (ref IOException error) {}
        return 2;
    }
    try { return CompareFiles(argv[1], argv[2]); }
    catch (ref IOException error)
    {
        try
        {
            Console::Error.Write("Unable to compare files: ", 25);
            Console::Error.Write(argv[1], CString::Length(argv[1])); Console::Error.Write(" / ", 3);
            Console::Error.Write(argv[2], CString::Length(argv[2]));
            readonly(char)*? message = error.GetMessage();
            if (message != null) { Console::Error.Write(": ", 2); Console::Error.Write(message!, CString::Length(message!)); }
            Console::Error.WriteLine(); Console::FlushError();
        }
        catch (ref IOException diagnosticError) {}
        return 2;
    }
}
