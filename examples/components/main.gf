using std::Text;
using std::Memory;
using std::Collections;
using std;

void ReportLine(nuint line, readonly(char)* message)
{
    Console::Error.Write("Line ", 5);
    line.Format(Console::Error);
    Console::Error.Write(": ", 2);
    Console::Error.WriteLine(message);
    Console::FlushError();
}

int main(int argc, char** argv)
{
    nuint count = 0;
    if (argc != 2 || !TryParseNUInt(argv[1], &count))
    {
        Console::Error.WriteLine("Usage: components <node-count>");
        Console::FlushError();
        return 2;
    }
    nuint lineNumber = 0;
    bool writingOutput = false;
    try
    {
        DisjointSet sets = new(count);
        char[128] buffer;
        nuint length = 0;
        while (Console::TryReadLine(buffer, 128, ref length))
        {
            if (lineNumber == (nuint)0 - (nuint)1)
            {
                Console::Error.WriteLine("Input line count overflow");
                Console::FlushError();
                return 2;
            }
            lineNumber++;
            Span<readonly(char)> line = new(buffer, length);
            TextPartsAny parts = line.SplitAny(" \t", true);
            TextPartsAny::Enumerator tokens = parts.GetEnumerator();
            if (!tokens.MoveNext()) continue;
            Span<readonly(char)> first = tokens.Current;
            if (!tokens.MoveNext())
            { ReportLine(lineNumber, "Expected two node indices"); return 2; }
            Span<readonly(char)> second = tokens.Current;
            if (tokens.MoveNext())
            { ReportLine(lineNumber, "Expected two node indices"); return 2; }
            nuint a = 0;
            nuint b = 0;
            if (!TryParseNUInt(first.Data!, first.Length, &a)
                || !TryParseNUInt(second.Data!, second.Length, &b))
            { ReportLine(lineNumber, "Expected unsigned decimal node indices"); return 2; }
            if (a >= count || b >= count)
            { ReportLine(lineNumber, "Node index is outside the declared universe"); return 2; }
            sets.Union(a, b);
        }

        // Delay all stdout until the complete edge stream has validated.
        writingOutput = true;
        Console::Write("Components: ");
        sets.SetCount.Format(Console::Out);
        Console::WriteLine();
        for (nuint index = 0; index < count; index++)
        {
            index.Format(Console::Out);
            Console::Write(" ");
            sets.Find(index).Format(Console::Out);
            Console::WriteLine();
        }
        Console::Flush();
    }
    catch (ref BufferTooSmallException error)
    {
        if (lineNumber == (nuint)0 - (nuint)1)
        {
            Console::Error.WriteLine("Input line count overflow");
            Console::FlushError();
        }
        else ReportLine(lineNumber + 1, "Line exceeds the 127-byte payload limit");
        return 2;
    }
    catch (ref Exception error)
    {
        if (writingOutput) Console::Error.WriteLine("Unable to write components");
        else Console::Error.WriteLine("Unable to read or allocate components");
        Console::FlushError();
        return 1;
    }
    return 0;
}
