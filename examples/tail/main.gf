using std;

// Keep at most count owned lines, then print them in their original order.
int main(int argc, char** argv)
{
    nuint count = 10;
    if (argc < 2 || argc > 3)
    {
        Console::Error.WriteLine("Usage: tail <file> [count]");
        return 2;
    }
    readonly(char)* path = argv[1];
    if (argc == 3 && !TryParseNUInt(argv[2], &count))
    {
        Console::Error.WriteLine("tail: count must be a nonnegative decimal integer");
        return 2;
    }

    try
    {
        File input = File::OpenRead(path);
        File* source = &input;
        Queue<String> lines = new();
        if (count != 0)
        {
            foreach (String line in source.Lines())
            {
                // Evict before inserting, so the queue never exceeds count.
                if (lines.Count == count) lines.Dequeue();
                lines.Enqueue(move line);
            }
        }
        input.Close();

        while (!lines.IsEmpty)
        {
            String line = lines.Dequeue();
            Console::WriteLine(line.Data, line.Length);
        }
        Console::Flush();
        return 0;
    }
    catch (ref Exception error)
    {
        Console::Error.Write("tail: ", 6);
        Console::Error.WriteLine(error.GetMessage());
        return 1;
    }
}
