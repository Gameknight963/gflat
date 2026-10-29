using std::Memory;
namespace std::Collections;

/// Finds the first count consecutive elements matching the borrowed key, or returns Length.
/// Zero count returns zero; a count above Length returns Length without accessing pointers or callbacks.
/// Compares each examined element once from left to right, without element copies, library allocation or mutation.
/// Keep the span valid. The key must be readable and stable when 0 < count <= Length.
/// Preflight returns do not access key; callback exceptions and external effects propagate.
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
