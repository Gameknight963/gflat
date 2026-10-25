using std;

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
        {
            File* source = &input;
            FileLineSequence::Enumerator iterator = source.Lines().GetEnumerator();
            while (iterator.MoveNext())
            {
                immutable(String)* line = &iterator.Current;
                if (line.Contains(argv[2]))
                {
                    writingOutput = true;
                    Console::WriteLine(line.Data, line.Length);
                    writingOutput = false;
                }
            }
        }
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
