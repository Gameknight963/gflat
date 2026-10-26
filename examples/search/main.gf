using std::IO;
using std;

void EmitMatch(ref String line, immutable(char*) needle, bool endedWithLF, ref bool writingOutput)
{
    // Preserve bytes within a line; normalize its ending for Console output.
    if (endedWithLF && line.Length != 0 && line.Data[line.Length - 1] == '\r')
        line.Remove(line.Length - 1, 1);
    // Shared byte-oriented String search; no regex or Unicode normalization.
    if (line.Contains(needle))
    {
        writingOutput = true;
        Console::WriteLine(ref line);
        writingOutput = false;
    }
    line.Clear();
}

int main(int argc, char** argv)
{
    if (argc != 3)
    {
        Console::Error.WriteLine("Usage: search <file> <text>");
        return 2;
    }
    bool writingOutput = false;
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
                if (buffer[i] == '\n') EmitMatch(ref line, argv[2], true, ref writingOutput);
                else line.Append(&buffer[i], 1);
            }
        }
        if (line.Length != 0) EmitMatch(ref line, argv[2], false, ref writingOutput);
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
