using std::Numerics;
using std::Collections;
using std;

alias Timer = std::Stopwatch<NativeClock>;

int CompareValues(immutable(int)* left, immutable(int)* right) nothrow
{
    if (*left < *right) return -1;
    if (*left > *right) return 1;
    return 0;
}

void WriteDuration(readonly(char)* label, TimeSpan duration)
{
    Console::Write(label);
    duration.Format(Console::Out);
    Console::WriteLine();
}

int main(int argc, char** argv)
{
    nuint count = 100000;
    try
    {
        if (argc > 2 || (argc == 2 && !TryParseNUInt(argv[1], &count)) || count > 1000000)
        {
            Console::Error.WriteLine("Usage: sort-bench [count: 0..1000000]");
            Console::FlushError();
            return 2;
        }
        Vector<int> first = new();
        Vector<int> second = new();
        first.Reserve(count);
        second.Reserve(count);
        PseudoRandom random = new(17ul);
        long inputChecksum = 0l;
        for (nuint i = 0; i < count; i++)
        {
            int value = random.NextInt();
            first.Add(value);
            second.Add(value);
            inputChecksum += (long)value;
        }

        Timer timer = Timer::StartNew();
        Sort<int>(first.AsSpan(), &CompareValues);
        timer.Stop();
        TimeSpan sortDuration = timer.Elapsed;
        timer.Restart();
        StableSort<int>(second.AsSpan(), &CompareValues);
        timer.Stop();
        TimeSpan stableSortDuration = timer.Elapsed;

        long checksum = 0l;
        for (nuint i = 0; i < count; i++)
        {
            if (first[i] != second[i] || (i != 0 && (first[i - 1] > first[i] || second[i - 1] > second[i])))
            {
                Console::Error.WriteLine("Sort verification failed");
                Console::FlushError();
                return 3;
            }
            // At most one million signed 32-bit values: the sum fits long.
            checksum += (long)first[i];
        }
        if (checksum != inputChecksum)
        {
            Console::Error.WriteLine("Sort verification failed");
            Console::FlushError();
            return 3;
        }
        Console::Write("Count: ");
        count.Format(Console::Out);
        Console::WriteLine();
        Console::Write("Checksum: ");
        checksum.Format(Console::Out);
        Console::WriteLine();
        WriteDuration("Sort: ", sortDuration);
        WriteDuration("StableSort: ", stableSortDuration);
        Console::Flush();
        return 0;
    }
    catch (ref Exception error)
    {
        Console::Error.WriteLine("Unable to allocate, time, or report sorting");
        Console::FlushError();
        return 1;
    }
}
