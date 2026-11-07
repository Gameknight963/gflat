namespace Standard::Collections;

public bool Contains<T>(immutable(Stack<T>)* source, immutable(T)* key,
    bool(immutable(T)*, immutable(T)*)* equal)
    => source.ContainsBy(key, equal);

public bool Contains<T : IEquatable<T>>(immutable(Stack<T>)* source, immutable(T)* key)
    => source.ContainsBy(key, EqualityComparer<T>::Equals);

extension Stack<T>
{
    public immutable bool Contains(immutable(T)* key, bool(immutable(T)*, immutable(T)*)* equal)
        => Standard::Collections::Contains<T>(this, key, equal);
}

extension Stack<T : IEquatable<T>>
{
    public immutable bool Contains(immutable(T)* key)
        => Standard::Collections::Contains<T>(this, key);
}
