using Standard::Text;
using Standard::IO;
using Standard;

void EmitMatch(ref String line, immutable(char*) needle, nuint needleLength, bool ignoreCase, bool endedWithLF, ref bool writingOutput)
{
    // Preserve bytes within a line; normalize its ending for Console output.
    if (endedWithLF && line.Length != 0 && line.Data[line.Length - 1] == '\r')
        line.Remove(line.Length - 1, 1);
    // ASCII folding preserves every other byte and never copies or edits the line.
    bool matches = false;
    if (ignoreCase)
        matches = needleLength == 0 || Ascii::IndexOfIgnoreCase(line.Data, line.Length, needle, needleLength) != line.Length;
    else matches = line.IndexOf(needle, needleLength) != String::NotFound;
    if (matches)
    {
        writingOutput = true;
        Console::WriteLine(ref line);
        writingOutput = false;
    }
    line.Clear();
}

int main(int argc, char** argv)
{
    bool ignoreCase = false;
    int fileIndex = 1;
    int textIndex = 2;
    if (argc == 4 && CString::Equals(argv[1], "--ignore-case"))
    {
        ignoreCase = true; fileIndex = 2; textIndex = 3;
    }
    else if (argc != 3)
    {
        Console::Error.WriteLine("Usage: search [--ignore-case] <file> <text>");
        return 2;
    }
    immutable(char*) needle = argv[textIndex];
    nuint needleLength = CString::Length(needle);
    bool writingOutput = false;
    try
    {
        File input = File::OpenRead(argv[fileIndex]);
        String line = new String();
        char[4096] buffer;
        while (true)
        {
            nuint count = input.Read(buffer, 4096);
            if (count == 0) break;
            for (nuint i = 0; i < count; i++)
            {
                if (buffer[i] == '\n') EmitMatch(ref line, needle, needleLength, ignoreCase, true, ref writingOutput);
                else line.Append(&buffer[i], 1);
            }
        }
        if (line.Length != 0) EmitMatch(ref line, needle, needleLength, ignoreCase, false, ref writingOutput);
        input.Close();
        writingOutput = true;
        Console::Flush();
        return 0;
    }
    catch (ref IOException error)
    {
        if (writingOutput) Console::Error.WriteLine("Unable to write search results.");
        else Console::Error.WriteLine("Unable to read file.");
        return 1;
    }
}
