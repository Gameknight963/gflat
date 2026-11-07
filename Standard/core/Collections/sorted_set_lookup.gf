namespace Standard::Collections;

public bool TryGetValue<T>(immutable(SortedSet<T>)* source, immutable(T)* query, ref T output)
{
    immutable(T)*? stored = source.Find(query);
    if (stored == null) return false;
    T copy = *stored!;
    output = move copy;
    return true;
}

extension SortedSet<T>
{
    public immutable bool TryGetValue(immutable(T)* query, ref T output)
        => Standard::Collections::TryGetValue<T>(this, query, ref output);
}
