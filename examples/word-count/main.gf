using std::IO;
using std::Collections;
using std;

// Count ASCII words without allocating for repeated occurrences.
void RecordWord(HashMap<String, nuint>* counts, ref String word)
{
    if (word.IsEmpty) return;
    nuint*? existing = counts.Find(&word);
    if (existing != null) (*existing!)++;
    else counts.Add(word.Clone(), 1);
    word.Clear();
}

void ReadWords(readonly(char)* path, HashMap<String, nuint>* counts)
{
    String word = new();
    File input = File::OpenRead(path);
    char[4096] buffer;
    while (true)
    {
        nuint read = input.Read(buffer, 4096);
        if (read == 0) break;
        for (nuint i = 0; i < read; i++)
        {
            char value = buffer[i];
            if (value >= 'A' && value <= 'Z') value = (char)(value - 'A' + 'a');
            if (value >= 'a' && value <= 'z') word.Append(value);
            else RecordWord(counts, ref word);
        }
    }
    RecordWord(counts, ref word);
    input.Close();
}

int main(int argc, char** argv)
{
    if (argc != 2)
    {
        Console::Error.WriteLine("Usage: word-count <file>");
        return 2;
    }
    HashMap<String, nuint> counts = new();
    try { ReadWords(argv[1], &counts); }
    catch (ref IOException error)
    {
        Console::Error.WriteLine("Unable to read input file.");
        return 1;
    }
    try
    {
        HashMap<String, nuint>::Enumerator iterator = counts.GetEnumerator();
        while (iterator.MoveNext())
        {
            immutable(String)* key = iterator.Current.Key;
            nuint count = *iterator.Current.ValueImmutable;
            Console::Write(key.Data, key.Length);
            Console::Write(": ", 2);
            count.Format(Console::Out);
            Console::WriteLine();
        }
        Console::Flush();
        return 0;
    }
    catch (ref IOException error)
    {
        Console::Error.WriteLine("Unable to write word counts.");
        return 1;
    }
}
