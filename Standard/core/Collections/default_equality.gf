using Standard::Memory;
namespace Standard::Collections;

// Optional equality-contract conveniences; explicit callback APIs stay independent.
public bool SequenceEqual<T : IEquatable<T>>(Span<immutable(T)> first, Span<immutable(T)> second)
    => Standard::Collections::SequenceEqual<T>(first, second, EqualityComparer<T>::Equals);

public bool StartsWith<T : IEquatable<T>>(Span<immutable(T)> values, Span<immutable(T)> prefix)
    => Standard::Collections::StartsWith<T>(values, prefix, EqualityComparer<T>::Equals);

public bool EndsWith<T : IEquatable<T>>(Span<immutable(T)> values, Span<immutable(T)> suffix)
    => Standard::Collections::EndsWith<T>(values, suffix, EqualityComparer<T>::Equals);

public nuint CommonPrefixLength<T : IEquatable<T>>(Span<immutable(T)> first, Span<immutable(T)> second)
    => Standard::Collections::CommonPrefixLength<T>(first, second, EqualityComparer<T>::Equals);

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
