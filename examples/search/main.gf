using std;

// Byte-oriented substring search; no regex or Unicode normalization.
bool Contains(ref readonly(String) line, readonly(char*) needle)
{
    nuint length = 0;
    while (needle[length] != '\0') length++;
    if (length > line.Length) return false;
    for (nuint start = 0; start <= line.Length - length; start++)
    {
        nuint i = 0;
        while (i < length && line.Data[start + i] == needle[i]) i++;
        if (i == length) return true;
    }
    return false;
}

void EmitMatch(ref String line, readonly(char*) needle, bool endedWithLF)
{
    // Preserve bytes within a line; normalize its ending for Console output.
    if (endedWithLF && line.Length != 0 && line.Data[line.Length - 1] == '\r')
    {
        String trimmed = String::From(line.Data, line.Length - 1);
        if (Contains(ref trimmed, needle)) Console::WriteLine(ref trimmed);
    }
    else if (Contains(ref line, needle)) Console::WriteLine(ref line);
    line.Clear();
}

int main(int argc, char** argv)
{
    if (argc != 3)
    {
        Console::WriteLine("Usage: search <file> <text>");
        return 2;
    }
    try
    {
        File input = File::OpenRead(argv[1]);
        String line = new String();
        char[4096] buffer;
        while (true)
        {
            nuint count = input.Read(buffer, 4096);
            if (count == 0) break;
            for (nuint i = 0; i < count; i++)
            {
                if (buffer[i] == '\n') EmitMatch(ref line, argv[2], true);
                else line.Append(&buffer[i], 1);
            }
        }
        if (line.Length != 0) EmitMatch(ref line, argv[2], false);
        input.Close();
        return 0;
    }
    catch (IOException* error)
    {
        Console::WriteLine("Unable to read file.");
        return 1;
    }
}
