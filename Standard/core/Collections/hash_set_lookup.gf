namespace Standard::Collections;

public bool TryGetValue<T : IHashable>(immutable(HashSet<T>)* source, immutable(T)* query, ref T output)
{
    immutable(T)*? stored = source.Find(query);
    if (stored == null) return false;
    T copy = *stored!;
    output = move copy;
    return true;
}

extension HashSet<T : IHashable>
{
    public immutable bool TryGetValue(immutable(T)* query, ref T output)
        => Standard::Collections::TryGetValue<T>(this, query, ref output);
}
