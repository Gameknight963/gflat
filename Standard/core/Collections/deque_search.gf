namespace Standard::Collections;

public nuint IndexOf<T>(immutable(Deque<T>)* source, immutable(T)* key,
    bool(immutable(T)*, immutable(T)*)* equal)
{
    for (nuint i = 0; i < source.Count; i++)
        if (equal(&(*source)[i], key)) return i;
    return source.Count;
}

public nuint LastIndexOf<T>(immutable(Deque<T>)* source, immutable(T)* key,
    bool(immutable(T)*, immutable(T)*)* equal)
{
    nuint remaining = source.Count;
    while (remaining != 0)
    {
        remaining--;
        if (equal(&(*source)[remaining], key)) return remaining;
    }
    return source.Count;
}

public bool Contains<T>(immutable(Deque<T>)* source, immutable(T)* key,
    bool(immutable(T)*, immutable(T)*)* equal)
    => IndexOf<T>(source, key, equal) != source.Count;

public bool RemoveValue<T>(Deque<T>* source, immutable(T)* key,
    bool(immutable(T)*, immutable(T)*)* equal)
{
    nuint index = IndexOf<T>(source, key, equal);
    if (index == source.Count) return false;
    T removed = source.RemoveAt(index);
    return true;
}

public nuint IndexOf<T : IEquatable<T>>(immutable(Deque<T>)* source, immutable(T)* key)
    => IndexOf<T>(source, key, EqualityComparer<T>::Equals);

public nuint LastIndexOf<T : IEquatable<T>>(immutable(Deque<T>)* source, immutable(T)* key)
    => LastIndexOf<T>(source, key, EqualityComparer<T>::Equals);

public bool Contains<T : IEquatable<T>>(immutable(Deque<T>)* source, immutable(T)* key)
    => Contains<T>(source, key, EqualityComparer<T>::Equals);

public bool RemoveValue<T : IEquatable<T>>(Deque<T>* source, immutable(T)* key)
    => RemoveValue<T>(source, key, EqualityComparer<T>::Equals);

extension Deque<T>
{
    public immutable nuint IndexOf(immutable(T)* key, bool(immutable(T)*, immutable(T)*)* equal)
        => Standard::Collections::IndexOf<T>(this, key, equal);

    public immutable nuint LastIndexOf(immutable(T)* key, bool(immutable(T)*, immutable(T)*)* equal)
        => Standard::Collections::LastIndexOf<T>(this, key, equal);

    public immutable bool Contains(immutable(T)* key, bool(immutable(T)*, immutable(T)*)* equal)
        => Standard::Collections::Contains<T>(this, key, equal);

    public bool RemoveValue(immutable(T)* key, bool(immutable(T)*, immutable(T)*)* equal)
        => Standard::Collections::RemoveValue<T>(this, key, equal);

}

extension Deque<T : IEquatable<T>>
{
    public immutable nuint IndexOf(immutable(T)* key)
        => Standard::Collections::IndexOf<T>(this, key);

    public immutable nuint LastIndexOf(immutable(T)* key)
        => Standard::Collections::LastIndexOf<T>(this, key);

    public immutable bool Contains(immutable(T)* key)
        => Standard::Collections::Contains<T>(this, key);

    public bool RemoveValue(immutable(T)* key)
        => Standard::Collections::RemoveValue<T>(this, key);

}
