using std::Collections;
using std;

int main()
{
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
            Console::WriteLine(lines[i].Data, lines[i].Length);
        Console::Flush();
    }
    catch (ref Exception error)
    {
        Console::Error.WriteLine(error.GetMessage());
        return 1;
    }
    return 0;
}
