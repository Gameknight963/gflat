using Standard::Text;
using Standard::Collections;
using Standard;

int main(int argc, char** argv)
{
    bool unique = false;
    if (argc == 2 && CString::Equals(argv[1], "--unique")) unique = true;
    else if (argc != 1)
    {
        Console::Error.WriteLine("Usage: sort-lines [--unique]");
        return 2;
    }
    try
    {
        Vector<String> lines = new();
        String line = new();
        while (Console::TryReadLine(ref line))
        {
            lines.Add(move line);
            line = new String();
        }
        Sort(lines.AsSpan());
        for (nuint i = 0; i < lines.Count; i++)
        {
            if (unique && i > 0 && lines[i].Equals(&lines[i - 1])) continue;
            Console::WriteLine(lines[i].Data, lines[i].Length);
        }
        Console::Flush();
    }
    catch (ref Exception error)
    {
        Console::Error.WriteLine(error.GetMessage());
        return 1;
    }
    return 0;
}
