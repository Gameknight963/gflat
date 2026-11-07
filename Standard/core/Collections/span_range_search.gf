using Standard::Memory;

namespace Standard::Collections;

public nuint IndexOfAnyInRange<T>(Span<immutable(T)> values, immutable(T)* lower, immutable(T)* upper,
    int(immutable(T)*, immutable(T)*)* compare)
{
    for (nuint i = 0; i < values.Length; i++)
    {
        if (compare(&values[i], lower) >= 0 && compare(&values[i], upper) <= 0) return i;
    }
    return values.Length;
}

public nuint IndexOfAnyExceptInRange<T>(Span<immutable(T)> values, immutable(T)* lower, immutable(T)* upper,
    int(immutable(T)*, immutable(T)*)* compare)
{
    for (nuint i = 0; i < values.Length; i++)
    {
        if (compare(&values[i], lower) < 0 || compare(&values[i], upper) > 0) return i;
    }
    return values.Length;
}

public nuint LastIndexOfAnyInRange<T>(Span<immutable(T)> values, immutable(T)* lower, immutable(T)* upper,
    int(immutable(T)*, immutable(T)*)* compare)
{
    nuint remaining = values.Length;
    while (remaining != 0)
    {
        remaining--;
        if (compare(&values[remaining], lower) >= 0 && compare(&values[remaining], upper) <= 0) return remaining;
    }
    return values.Length;
}

public nuint LastIndexOfAnyExceptInRange<T>(Span<immutable(T)> values, immutable(T)* lower, immutable(T)* upper,
    int(immutable(T)*, immutable(T)*)* compare)
{
    nuint remaining = values.Length;
    while (remaining != 0)
    {
        remaining--;
        if (compare(&values[remaining], lower) < 0 || compare(&values[remaining], upper) > 0) return remaining;
    }
    return values.Length;
}

public bool ContainsAnyInRange<T>(Span<immutable(T)> values, immutable(T)* lower, immutable(T)* upper,
    int(immutable(T)*, immutable(T)*)* compare)
    => Standard::Collections::IndexOfAnyInRange<T>(values, lower, upper, compare) != values.Length;

public bool ContainsAnyExceptInRange<T>(Span<immutable(T)> values, immutable(T)* lower, immutable(T)* upper,
    int(immutable(T)*, immutable(T)*)* compare)
    => Standard::Collections::IndexOfAnyExceptInRange<T>(values, lower, upper, compare) != values.Length;

