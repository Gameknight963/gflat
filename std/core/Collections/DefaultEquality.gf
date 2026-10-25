namespace std;

// Optional equality-contract conveniences; explicit callback APIs stay independent.
public bool SequenceEqual<T : IEquatable<T>>(Span<immutable(T)> first, Span<immutable(T)> second)
    => std::SequenceEqual<T>(first, second, EqualityComparer<T>::Equals);

public nuint IndexOf<T : IEquatable<T>>(Span<immutable(T)> values, immutable(T)* key)
    => std::IndexOf<T>(values, key, EqualityComparer<T>::Equals);

public nuint LastIndexOf<T : IEquatable<T>>(Span<immutable(T)> values, immutable(T)* key)
    => std::LastIndexOf<T>(values, key, EqualityComparer<T>::Equals);

public bool Contains<T : IEquatable<T>>(Span<immutable(T)> values, immutable(T)* key)
    => std::Contains<T>(values, key, EqualityComparer<T>::Equals);
