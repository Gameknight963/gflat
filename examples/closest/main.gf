using std;

// Rank borrowed command-line bytes; retain only indices of the closest matches.
int main(int argc, char** argv)
{
    if (argc < 3)
    {
        Console::Error.WriteLine("Usage: closest <query> <candidate> [more candidates...]");
        return 2;
    }
    try
    {
        nuint queryLength = CString::Length(argv[1]);
        Span<readonly(char)> query = new(argv[1], queryLength);
        Vector<nuint> row = new();
        Vector<int> winners = new();
        nuint best = (nuint)0 - (nuint)1;
        for (int index = 2; index < argc; index++)
        {
            nuint candidateLength = CString::Length(argv[index]);
            nuint required = 0;
            if (queryLength != 0 && candidateLength != 0)
            {
                nuint shorter = queryLength;
                if (candidateLength < shorter) shorter = candidateLength;
                if (shorter == (nuint)0 - (nuint)1)
                    throw new Exception("Edit distance workspace length overflow");
                required = shorter + 1;
            }
            while (row.Count < required) row.Add((nuint)0);
            Span<readonly(char)> candidate = new(argv[index], candidateLength);
            nuint distance = 0;
            if (!EditDistance::TryCompute(query, candidate, row.AsSpan(), ref distance))
                throw new Exception("Edit distance workspace is too short");
            if (distance < best) { best = distance; winners.Clear(); }
            if (distance == best) winners.Add(index);
        }

        // Finish ranking before writing; each candidate remains borrowed from argv.
        for (nuint match = 0; match < winners.Count; match++)
        {
            best.Format(Console::Out);
            Console::Write("\t", 1);
            readonly(char)* candidate = argv[winners[match]];
            Console::WriteLine(candidate, CString::Length(candidate));
        }
        Console::Flush();
        return 0;
    }
    catch (ref Exception error)
    {
        Console::Error.WriteLine("closest: Unable to compare candidates or write output");
        return 1;
    }
}
