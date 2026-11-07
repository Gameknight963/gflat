namespace Standard::Collections;

public bool ContainsValue<K : IHashable, V>(immutable(HashMap<K, V>)* source, immutable(V)* value,
    bool(immutable(V)*, immutable(V)*)* equal)
{
    var iterator = source.GetEnumerator();
    while (iterator.MoveNext())
        if (equal(iterator.Current.ValueImmutable, value)) return true;
    return false;
}

public bool ContainsValue<K : IHashable, V : IEquatable<V>>(immutable(HashMap<K, V>)* source, immutable(V)* value)
    => ContainsValue<K, V>(source, value, EqualityComparer<V>::Equals);

extension HashMap<K : IHashable, V>
{
    public immutable bool ContainsValue(immutable(V)* value, bool(immutable(V)*, immutable(V)*)* equal)
        => Standard::Collections::ContainsValue<K, V>(this, value, equal);
}

extension HashMap<K : IHashable, V : IEquatable<V>>
{
    public immutable bool ContainsValue(immutable(V)* value)
        => Standard::Collections::ContainsValue<K, V>(this, value);
}
