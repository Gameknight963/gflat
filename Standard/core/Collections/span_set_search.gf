using Standard::Memory;

namespace Standard::Collections;

namespace span_set_search_detail
{
    private bool Matches<T>(immutable(T)* value, Span<immutable(T)> candidates,
        bool(immutable(T)*, immutable(T)*)* equal)
    {
        for (nuint i = 0; i < candidates.Length; i++)
            if (equal(value, &candidates[i])) return true;
        return false;
    }
}

public nuint IndexOfAny<T>(Span<immutable(T)> values, Span<immutable(T)> candidates,
    bool(immutable(T)*, immutable(T)*)* equal)
{
    if (candidates.IsEmpty) return values.Length;
    for (nuint i = 0; i < values.Length; i++)
    {
        if (span_set_search_detail::Matches<T>(&values[i], candidates, equal)) return i;
    }
    return values.Length;
}

public nuint IndexOfAnyExcept<T>(Span<immutable(T)> values, Span<immutable(T)> candidates,
    bool(immutable(T)*, immutable(T)*)* equal)
{
    if (values.IsEmpty) return values.Length;
    if (candidates.IsEmpty) return 0;
    for (nuint i = 0; i < values.Length; i++)
    {
        if (!span_set_search_detail::Matches<T>(&values[i], candidates, equal)) return i;
    }
    return values.Length;
}

public nuint LastIndexOfAny<T>(Span<immutable(T)> values, Span<immutable(T)> candidates,
    bool(immutable(T)*, immutable(T)*)* equal)
{
    if (candidates.IsEmpty) return values.Length;
    nuint remaining = values.Length;
    while (remaining != 0)
    {
        remaining--;
        if (span_set_search_detail::Matches<T>(&values[remaining], candidates, equal)) return remaining;
    }
    return values.Length;
}

public nuint LastIndexOfAnyExcept<T>(Span<immutable(T)> values, Span<immutable(T)> candidates,
    bool(immutable(T)*, immutable(T)*)* equal)
{
    if (values.IsEmpty) return values.Length;
    if (candidates.IsEmpty) return values.Length - 1;
    nuint remaining = values.Length;
    while (remaining != 0)
    {
        remaining--;
        if (!span_set_search_detail::Matches<T>(&values[remaining], candidates, equal)) return remaining;
    }
    return values.Length;
}

public bool ContainsAny<T>(Span<immutable(T)> values, Span<immutable(T)> candidates,
    bool(immutable(T)*, immutable(T)*)* equal)
    => Standard::Collections::IndexOfAny<T>(values, candidates, equal) != values.Length;

public bool ContainsAnyExcept<T>(Span<immutable(T)> values, Span<immutable(T)> candidates,
    bool(immutable(T)*, immutable(T)*)* equal)
    => Standard::Collections::IndexOfAnyExcept<T>(values, candidates, equal) != values.Length;

