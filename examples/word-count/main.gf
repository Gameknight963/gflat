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

int main(int argc, char** argv)
{
    if (argc != 2)
    {
        Console::WriteLine("Usage: word-count <file>");
        return 2;
    }
    try
    {
        HashMap<String, nuint> counts = new();
        String word = new();
        File input = File::OpenRead(argv[1]);
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
                else RecordWord(&counts, ref word);
            }
        }
        RecordWord(&counts, ref word);
        input.Close();

        HashMap<String, nuint>::Enumerator iterator = counts.GetEnumerator();
        while (iterator.MoveNext())
        {
            immutable(String)* key = iterator.Current.Key;
            nuint count = *iterator.Current.ValueImmutable;
            String line = s"";
            line.Append(key);
            line.Append(':');
            line.Append(' ');
            count.Format(&line);
            Console::WriteLine(ref line);
        }
        return 0;
    }
    catch (IOException* error)
    {
        Console::WriteLine("Unable to read input file.");
        return 1;
    }
}
