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
