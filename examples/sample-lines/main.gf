using std;

int main(int argc, char** argv)
{
    if (argc < 3 || argc > 4)
    {
        Console::Error.WriteLine("Usage: sample-lines <input-file> <count> [seed]");
        return 2;
    }
    nuint count = 0;
    if (!TryParseNUInt(argv[2], &count))
    {
        Console::Error.WriteLine("sample-lines: count must be a nonnegative decimal integer");
        return 2;
    }
    ulong seed = 0ul;
    if (argc == 4 && !TryParseULong(argv[3], &seed))
    {
        Console::Error.WriteLine("sample-lines: seed must be an unsigned 64-bit decimal integer");
        return 2;
    }
    if (count == 0) return 0;

    try
    {
        Vector<String> sample = new();
        PseudoRandom random = new(seed);
        {
            File input = File::OpenRead(argv[1]);
            File* source = &input;
            ulong seen = 0ul;
            ulong maximum = (ulong)0 - (ulong)1;
            {
                FileLineSequence::Enumerator iterator = source.Lines().GetEnumerator();
                while (iterator.MoveNext())
                {
                    if (seen == maximum) throw new Exception("Input line count overflow");
                    seen++;
                    if (sample.Count < count)
                    {
                        String line = iterator.Current.Clone();
                        sample.Add(move line);
                    }
                    else
                    {
                        ulong selected = random.NextULong(seen);
                        if (selected < (ulong)count)
                        {
                            String line = iterator.Current.Clone();
                            sample[(nuint)selected] = move line;
                        }
                    }
                }
            }
            input.Close();
        }
        Shuffle(sample.AsSpan(), ref random);
        for (nuint index = 0; index < sample.Count; index++)
            Console::WriteLine(sample[index].Data, sample[index].Length);
        Console::Flush();
        return 0;
    }
    catch (ref Exception error)
    {
        Console::Error.Write("sample-lines: ", 14);
        Console::Error.WriteLine(error.GetMessage());
        return 1;
    }
}
