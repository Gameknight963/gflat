using std::IO;
using std;

// Emit one JSON string literal per input line, without a second owning buffer.
int main(int argc, char** argv)
{
    if (argc != 2)
    {
        Console::Error.WriteLine("Usage: quote-lines <input-file>");
        return 2;
    }
    try
    {
        File input = File::OpenRead(argv[1]);
        File* source = &input;
        FileLineSequence::Enumerator lines = source.Lines().GetEnumerator();
        while (lines.MoveNext())
        {
            Console::Out.WriteJsonString(lines.Current.Data, lines.Current.Length);
            Console::WriteLine();
        }
        input.Close();
        Console::Flush();
    }
    catch (ref Exception error)
    {
        Console::Error.WriteLine(error.GetMessage());
        return 1;
    }
    return 0;
}
