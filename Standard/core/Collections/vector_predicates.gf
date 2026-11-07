namespace Standard::Collections;

public nuint FindIndex<T>(immutable(Vector<T>)* source, bool(immutable(T)*)* predicate)
{
    for (nuint i = 0; i < source.Count; i++)
        if (predicate(&(*source)[i])) return i;
    return source.Count;
}

public nuint FindLastIndex<T>(immutable(Vector<T>)* source, bool(immutable(T)*)* predicate)
{
    nuint remaining = source.Count;
    while (remaining != 0)
    {
        remaining--;
        if (predicate(&(*source)[remaining])) return remaining;
    }
    return source.Count;
}

public immutable(T)*? Find<T>(immutable(Vector<T>)* source, bool(immutable(T)*)* predicate)
{
    nuint index = Standard::Collections::FindIndex<T>(source, predicate);
    if (index == source.Count) return null;
    return &(*source)[index];
}

public immutable(T)*? FindLast<T>(immutable(Vector<T>)* source, bool(immutable(T)*)* predicate)
{
    nuint index = Standard::Collections::FindLastIndex<T>(source, predicate);
    if (index == source.Count) return null;
    return &(*source)[index];
}

public bool Exists<T>(immutable(Vector<T>)* source, bool(immutable(T)*)* predicate)
    => Standard::Collections::FindIndex<T>(source, predicate) != source.Count;

public bool TrueForAll<T>(immutable(Vector<T>)* source, bool(immutable(T)*)* predicate)
{
    for (nuint i = 0; i < source.Count; i++)
        if (!predicate(&(*source)[i])) return false;
    return true;
}

extension Vector<T>
{
    public immutable nuint FindIndex(bool(immutable(T)*)* predicate)
        => Standard::Collections::FindIndex<T>(this, predicate);

    public immutable nuint FindLastIndex(bool(immutable(T)*)* predicate)
        => Standard::Collections::FindLastIndex<T>(this, predicate);

    public immutable immutable(T)*? Find(bool(immutable(T)*)* predicate)
        => Standard::Collections::Find<T>(this, predicate);

    public immutable immutable(T)*? FindLast(bool(immutable(T)*)* predicate)
        => Standard::Collections::FindLast<T>(this, predicate);

    public immutable bool Exists(bool(immutable(T)*)* predicate)
        => Standard::Collections::Exists<T>(this, predicate);

    public immutable bool TrueForAll(bool(immutable(T)*)* predicate)
        => Standard::Collections::TrueForAll<T>(this, predicate);

}
