using Standard::Memory;

namespace Standard::Collections;

// Optional equality-contract conveniences; explicit callback APIs stay independent.
public void Replace<T : IEquatable<T>>(Span<T> values, T oldValue, T newValue)
    => Standard::Collections::Replace<T>(values, move oldValue, move newValue, EqualityComparer<T>::Equals);

public void Replace<T : IEquatable<T>>(Span<immutable(T)> source, Span<T> destination, T oldValue, T newValue)
    => Standard::Collections::Replace<T>(source, destination, move oldValue, move newValue, EqualityComparer<T>::Equals);

extension Span<T : IEquatable<T>>
{
    public readonly void Replace(T oldValue, T newValue)
        => Standard::Collections::Replace<T>(*this, move oldValue, move newValue);
}

extension Span<immutable(T) : IEquatable<T>>
{
    public immutable void Replace(Span<T> destination, T oldValue, T newValue)
        => Standard::Collections::Replace<T>(*this, destination, move oldValue, move newValue);
}

public bool SequenceEqual<T : IEquatable<T>>(Span<immutable(T)> first, Span<immutable(T)> second)
    => Standard::Collections::SequenceEqual<T>(first, second, EqualityComparer<T>::Equals);

public bool StartsWith<T : IEquatable<T>>(Span<immutable(T)> values, Span<immutable(T)> prefix)
    => Standard::Collections::StartsWith<T>(values, prefix, EqualityComparer<T>::Equals);

public bool EndsWith<T : IEquatable<T>>(Span<immutable(T)> values, Span<immutable(T)> suffix)
    => Standard::Collections::EndsWith<T>(values, suffix, EqualityComparer<T>::Equals);

public nuint CommonPrefixLength<T : IEquatable<T>>(Span<immutable(T)> first, Span<immutable(T)> second)
    => Standard::Collections::CommonPrefixLength<T>(first, second, EqualityComparer<T>::Equals);

public nuint CommonSuffixLength<T : IEquatable<T>>(Span<immutable(T)> first, Span<immutable(T)> second)
    => Standard::Collections::CommonSuffixLength<T>(first, second, EqualityComparer<T>::Equals);

public nuint AdjacentFind<T : IEquatable<T>>(Span<immutable(T)> values)
    => Standard::Collections::AdjacentFind<T>(values, EqualityComparer<T>::Equals);

public nuint IndexOf<T : IEquatable<T>>(Span<immutable(T)> values, immutable(T)* key)
    => Standard::Collections::IndexOf<T>(values, key, EqualityComparer<T>::Equals);

public nuint LastIndexOf<T : IEquatable<T>>(Span<immutable(T)> values, immutable(T)* key)
    => Standard::Collections::LastIndexOf<T>(values, key, EqualityComparer<T>::Equals);

public bool Contains<T : IEquatable<T>>(Span<immutable(T)> values, immutable(T)* key)
    => Standard::Collections::Contains<T>(values, key, EqualityComparer<T>::Equals);

public nuint FindRun<T : IEquatable<T>>(Span<immutable(T)> values, nuint count, immutable(T)* key)
    => Standard::Collections::FindRun<T>(values, count, key, EqualityComparer<T>::Equals);

public nuint IndexOf<T : IEquatable<T>>(Span<immutable(T)> values, Span<immutable(T)> pattern)
    => Standard::Collections::IndexOf<T>(values, pattern, EqualityComparer<T>::Equals);

public nuint LastIndexOf<T : IEquatable<T>>(Span<immutable(T)> values, Span<immutable(T)> pattern)
    => Standard::Collections::LastIndexOf<T>(values, pattern, EqualityComparer<T>::Equals);

public nuint Count<T : IEquatable<T>>(Span<immutable(T)> values, immutable(T)* key)
    => Standard::Collections::Count<T>(values, key, EqualityComparer<T>::Equals);

public nuint Count<T : IEquatable<T>>(Span<immutable(T)> values, Span<immutable(T)> pattern)
    => Standard::Collections::Count<T>(values, pattern, EqualityComparer<T>::Equals);

public nuint IndexOfAny<T : IEquatable<T>>(Span<immutable(T)> values, Span<immutable(T)> candidates)
    => Standard::Collections::IndexOfAny<T>(values, candidates, EqualityComparer<T>::Equals);

public nuint LastIndexOfAny<T : IEquatable<T>>(Span<immutable(T)> values, Span<immutable(T)> candidates)
    => Standard::Collections::LastIndexOfAny<T>(values, candidates, EqualityComparer<T>::Equals);

public nuint IndexOfAnyExcept<T : IEquatable<T>>(Span<immutable(T)> values, Span<immutable(T)> candidates)
    => Standard::Collections::IndexOfAnyExcept<T>(values, candidates, EqualityComparer<T>::Equals);

public nuint LastIndexOfAnyExcept<T : IEquatable<T>>(Span<immutable(T)> values, Span<immutable(T)> candidates)
    => Standard::Collections::LastIndexOfAnyExcept<T>(values, candidates, EqualityComparer<T>::Equals);

public bool ContainsAny<T : IEquatable<T>>(Span<immutable(T)> values, Span<immutable(T)> candidates)
    => Standard::Collections::ContainsAny<T>(values, candidates, EqualityComparer<T>::Equals);

public bool ContainsAnyExcept<T : IEquatable<T>>(Span<immutable(T)> values, Span<immutable(T)> candidates)
    => Standard::Collections::ContainsAnyExcept<T>(values, candidates, EqualityComparer<T>::Equals);
