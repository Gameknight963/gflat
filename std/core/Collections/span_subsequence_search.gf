using std::Memory;
namespace std::Collections;

/// Finds the first contiguous pattern match, or returns the source Length.
/// Empty patterns return zero; oversized patterns return Length without comparisons.
/// Both spans must remain valid and stable and may overlap. Elements are borrowed, source first.
/// Uses constant storage, no element copies or library allocation; callback exceptions propagate.
public nuint IndexOf<T>(Span<immutable(T)> values, Span<immutable(T)> pattern,
    bool(immutable(T)*, immutable(T)*)* equal)
{
    if (pattern.Length == 0) return 0;
    if (pattern.Length > values.Length) return values.Length;
    nuint last = values.Length - pattern.Length;
    for (nuint start = 0; start <= last; start++)
    {
        nuint offset = 0;
        while (offset < pattern.Length)
        {
            if (!equal(&values[start + offset], &pattern[offset])) break;
            offset++;
        }
        if (offset == pattern.Length) return start;
    }
    return values.Length;
}

/// Finds the last contiguous pattern match, or returns the source Length.
/// Empty patterns return Length as the reverse insertion endpoint; oversized patterns also return Length.
/// Candidates run right to left; each candidate compares offsets left to right, source element first.
/// Valid stable spans may overlap. No element copies or library allocation; callback exceptions propagate.
public nuint LastIndexOf<T>(Span<immutable(T)> values, Span<immutable(T)> pattern,
    bool(immutable(T)*, immutable(T)*)* equal)
{
    if (pattern.Length == 0) return values.Length;
    if (pattern.Length > values.Length) return values.Length;
    nuint start = values.Length - pattern.Length;
    while (true)
    {
        nuint offset = 0;
        while (offset < pattern.Length)
        {
            if (!equal(&values[start + offset], &pattern[offset])) break;
            offset++;
        }
        if (offset == pattern.Length) return start;
        if (start == 0) return values.Length;
        start--;
    }
}
