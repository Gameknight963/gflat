using std::Memory;
namespace std::Collections;

// Optional equality-contract conveniences; explicit callback APIs stay independent.
public bool SequenceEqual<T : IEquatable<T>>(Span<immutable(T)> first, Span<immutable(T)> second)
    => std::Collections::SequenceEqual<T>(first, second, EqualityComparer<T>::Equals);

public nuint CommonPrefixLength<T : IEquatable<T>>(Span<immutable(T)> first, Span<immutable(T)> second)
    => std::Collections::CommonPrefixLength<T>(first, second, EqualityComparer<T>::Equals);

public nuint AdjacentFind<T : IEquatable<T>>(Span<immutable(T)> values)
    => std::Collections::AdjacentFind<T>(values, EqualityComparer<T>::Equals);

public nuint IndexOf<T : IEquatable<T>>(Span<immutable(T)> values, immutable(T)* key)
    => std::Collections::IndexOf<T>(values, key, EqualityComparer<T>::Equals);

public nuint LastIndexOf<T : IEquatable<T>>(Span<immutable(T)> values, immutable(T)* key)
    => std::Collections::LastIndexOf<T>(values, key, EqualityComparer<T>::Equals);

public bool Contains<T : IEquatable<T>>(Span<immutable(T)> values, immutable(T)* key)
    => std::Collections::Contains<T>(values, key, EqualityComparer<T>::Equals);
