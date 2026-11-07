using Standard::Memory;

namespace Standard::Collections;

// Finds the first contiguous pattern, or returns the source Length.
// An empty pattern returns zero. An oversized pattern fails before comparisons.
// The spans may overlap. Comparisons receive the source element first.
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

// Finds the last contiguous pattern, or returns the source Length.
// Empty and oversized patterns return Length. Candidates run right to left, with offsets compared left to right.
// The spans may overlap. Comparisons receive the source element first.
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

// Counts non-overlapping patterns. Empty and oversized patterns match zero times.
public nuint Count<T>(Span<immutable(T)> values, Span<immutable(T)> pattern,
    bool(immutable(T)*, immutable(T)*)* equal)
{
    if (pattern.Length == 0 || pattern.Length > values.Length) return 0;
    nuint count = 0;
    nuint start = 0;
    nuint last = values.Length - pattern.Length;
    while (start <= last)
    {
        nuint offset = 0;
        while (offset < pattern.Length)
        {
            if (!equal(&values[start + offset], &pattern[offset])) break;
            offset++;
        }
        if (offset == pattern.Length)
        {
            count++;
            start = start + pattern.Length;
        }
        else start++;
    }
    return count;
}
