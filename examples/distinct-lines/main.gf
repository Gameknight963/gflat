using Standard::Collections;
using Standard::IO;
using Standard::Text;

int main(int argc, char** argv)
{
    if (argc != 1)
    {
        Console::Error.WriteLine("Usage: distinct-lines");
        return 2;
    }
    try
    {
        char[4096] scratch;
        BufferedReader input = new(Console::In, scratch, 4096);
        HashSet<String> seen = new();
        LineSequence lines = input.EnumerateLines();
        LineSequence::Enumerator iterator = lines.GetEnumerator();
        while (iterator.MoveNext())
        {
            immutable(String)* line = &iterator.Current;
            if (seen.Contains(line)) continue;
            // Copy only a new line into the set; repeated lines remain borrowed.
            seen.Add(*line);
            Console::WriteLine(line.Data, line.Length);
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
