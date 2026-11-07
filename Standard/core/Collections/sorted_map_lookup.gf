namespace Standard::Collections;

// Copying conveniences are separate from the map's borrowed lookup operations.
public bool TryGetValue<K, V>(immutable(SortedMap<K, V>)* source, immutable(K)* key, ref V output)
{
    immutable(V)*? found = source.FindImmutable(key);
    if (found == null) return false;
    V copy = *found!;
    output = move copy;
    return true;
}

public V GetValueOrDefault<K, V>(immutable(SortedMap<K, V>)* source, immutable(K)* key, V fallback)
{
    immutable(V)*? found = source.FindImmutable(key);
    if (found == null) return move fallback;
    return *found!;
}

public V GetValueOrDefault<K, V>(immutable(SortedMap<K, V>)* source, immutable(K)* key)
{
    immutable(V)*? found = source.FindImmutable(key);
    if (found == null) return new V();
    return *found!;
}

extension SortedMap<K, V>
{
    public immutable bool TryGetValue(immutable(K)* key, ref V output)
        => Standard::Collections::TryGetValue<K, V>(this, key, ref output);

    public immutable V GetValueOrDefault(immutable(K)* key, V fallback)
        => Standard::Collections::GetValueOrDefault<K, V>(this, key, move fallback);

    public immutable V GetValueOrDefault(immutable(K)* key)
        => Standard::Collections::GetValueOrDefault<K, V>(this, key);
}
