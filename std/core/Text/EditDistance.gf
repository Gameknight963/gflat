using std::Memory;
namespace std;

// Unit-cost Levenshtein distance over counted bytes, using one caller-owned row.
public static class EditDistance
{
    private static nuint Increment(nuint value) nothrow
    {
        nuint maximum = (nuint)0 - (nuint)1;
        if (value == maximum) return maximum;
        return value + 1;
    }

    public static bool TryCompute(Span<readonly(char)> first, Span<readonly(char)> second,
        Span<nuint> scratch, ref nuint distance) nothrow
    {
        nuint firstLength = first.Length;
        nuint secondLength = second.Length;
        if (firstLength == 0) { distance = secondLength; return true; }
        if (secondLength == 0) { distance = firstLength; return true; }

        Span<readonly(char)> shorter = first;
        Span<readonly(char)> longer = second;
        nuint columns = firstLength;
        nuint rows = secondLength;
        if (columns > rows)
        {
            shorter = second; longer = first;
            columns = secondLength; rows = firstLength;
        }
        // Requires columns + 1 slots without overflowing that addition.
        if (scratch.Length <= columns) return false;
        // Valid nonempty spans supply readable/writable storage. Pointer access
        // avoids checked element indexers within these proven loop bounds.
        readonly(char)* horizontal = (readonly(char)*)shorter.Data;
        readonly(char)* vertical = (readonly(char)*)longer.Data;
        nuint* costs = (nuint*)scratch.Data;
        costs[0] = 0;
        for (nuint column = 0; column < columns; column++) costs[column + 1] = column + 1;
        for (nuint row = 0; row < rows; row++)
        {
            nuint diagonal = costs[0];
            costs[0] = row + 1;
            for (nuint column = 0; column < columns; column++)
            {
                nuint above = costs[column + 1];
                nuint candidate = diagonal;
                if (horizontal[column] != vertical[row]) candidate = Increment(candidate);
                nuint inserted = Increment(costs[column]);
                nuint deleted = Increment(above);
                if (inserted < candidate) candidate = inserted;
                if (deleted < candidate) candidate = deleted;
                costs[column + 1] = candidate;
                diagonal = above;
            }
        }
        distance = costs[columns];
        return true;
    }
}
