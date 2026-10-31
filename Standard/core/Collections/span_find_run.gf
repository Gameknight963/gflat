using Standard::Memory;

namespace Standard::Collections;

// Finds the first run of count elements equal to key, or returns Length.
// Zero count returns zero. A count above Length returns Length before reading key or calling equal.
// Comparisons run left to right with each examined element tested once.
public nuint FindRun<T>(Span<immutable(T)> values, nuint count, immutable(T)* key,
    bool(immutable(T)*, immutable(T)*)* equal)
{
    if (count == 0) return 0;
    if (count > values.Length) return values.Length;
    nuint run = 0;
    for (nuint index = 0; index < values.Length; index++)
    {
        if (equal(&values[index], key))
        {
            run++;
            if (run == count) return index - (count - 1);
        }
        else run = 0;
    }
    return values.Length;
}
