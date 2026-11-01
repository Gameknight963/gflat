using Standard;
using Standard::Collections;
using Standard::Compression;
using Standard::IO;
using Standard::Memory;
using Standard::Text;

const nuint MaximumArchiveBytes = 134217728;
const nuint MaximumEntryBytes = 16777216;

void Report(readonly(char)* context, readonly(char)* message)
{
    try
    {
        Console::Error.Write("zip-pack: ", 10);
        Console::Error.Write(context, CString::Length(context));
        Console::Error.Write(": ", 2);
        Console::Error.WriteLine(message);
        Console::FlushError();
    }
    catch (ref Exception ignored) { }
}

int main(int argc, char** argv)
{
    if (argc < 4 || (argc - 2) % 2 != 0)
    {
        Report("arguments", "usage: zip-pack output.zip name file [name file...]");
        return 2;
    }
    readonly(char)* context = argv[1];
    try
    {
        FileStream output = File::CreateNew(argv[1]);
        ZipArchiveWriter archive = new(&output);
        nuint archiveBytes = 22;
        for (int index = 2; index < argc; index += 2)
        {
            context = argv[index];
            nuint nameLength = CString::Length(argv[index]);
            if (nameLength == 0 || nameLength > 65535)
                throw new IOException("ZIP entry name must contain 1 to 65535 UTF-8 bytes");
            nuint overhead = 76 + 2 * nameLength;
            if (overhead > MaximumArchiveBytes - archiveBytes)
                throw new IOException("ZIP archive exceeds 128 MiB");
            context = argv[index + 1];
            Vector<byte> data = File::ReadAllBytes(argv[index + 1], MaximumEntryBytes);
            if (data.Count > MaximumArchiveBytes - archiveBytes - overhead)
                throw new IOException("ZIP archive exceeds 128 MiB");
            context = argv[index];
            archive.AddEntry(new Span<readonly(char)>(argv[index], nameLength), data.AsReadOnly());
            archiveBytes += overhead + data.Count;
        }
        context = argv[1];
        archive.Complete();
        output.Close();
        Console::Write("Packed ");
        Console::Write(archive.Count);
        if (archive.Count == 1) Console::WriteLine(" entry");
        else Console::WriteLine(" entries");
        Console::Flush();
    }
    catch (ref Exception error)
    {
        Report(context, error.GetMessage());
        return 1;
    }
    return 0;
}
