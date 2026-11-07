namespace Standard::Collections;

public LinkedList<T>::Node*? Find<T>(LinkedList<T>* source, immutable(T)* key, bool(immutable(T)*, immutable(T)*)* equal)
    => source.FindBy(key, equal);

public LinkedList<T>::Node*? Find<T : IEquatable<T>>(LinkedList<T>* source, immutable(T)* key)
    => source.FindBy(key, EqualityComparer<T>::Equals);

public LinkedList<T>::Node*? FindLast<T>(LinkedList<T>* source, immutable(T)* key, bool(immutable(T)*, immutable(T)*)* equal)
    => source.FindLastBy(key, equal);

public LinkedList<T>::Node*? FindLast<T : IEquatable<T>>(LinkedList<T>* source, immutable(T)* key)
    => source.FindLastBy(key, EqualityComparer<T>::Equals);

public bool Contains<T>(immutable(LinkedList<T>)* source, immutable(T)* key, bool(immutable(T)*, immutable(T)*)* equal)
    => source.ContainsBy(key, equal);

public bool Contains<T : IEquatable<T>>(immutable(LinkedList<T>)* source, immutable(T)* key)
    => source.ContainsBy(key, EqualityComparer<T>::Equals);

public bool RemoveValue<T>(LinkedList<T>* source, immutable(T)* key, bool(immutable(T)*, immutable(T)*)* equal)
    => source.RemoveValueBy(key, equal);

public bool RemoveValue<T : IEquatable<T>>(LinkedList<T>* source, immutable(T)* key)
    => source.RemoveValueBy(key, EqualityComparer<T>::Equals);

extension LinkedList<T>
{
    public LinkedList<T>::Node*? Find(immutable(T)* key, bool(immutable(T)*, immutable(T)*)* equal)
        => Standard::Collections::Find<T>(this, key, equal);

    public LinkedList<T>::Node*? FindLast(immutable(T)* key, bool(immutable(T)*, immutable(T)*)* equal)
        => Standard::Collections::FindLast<T>(this, key, equal);

    public immutable bool Contains(immutable(T)* key, bool(immutable(T)*, immutable(T)*)* equal)
        => Standard::Collections::Contains<T>(this, key, equal);

    public bool RemoveValue(immutable(T)* key, bool(immutable(T)*, immutable(T)*)* equal)
        => Standard::Collections::RemoveValue<T>(this, key, equal);
}

extension LinkedList<T : IEquatable<T>>
{
    public LinkedList<T>::Node*? Find(immutable(T)* key)
        => Standard::Collections::Find<T>(this, key);

    public LinkedList<T>::Node*? FindLast(immutable(T)* key)
        => Standard::Collections::FindLast<T>(this, key);

    public immutable bool Contains(immutable(T)* key)
        => Standard::Collections::Contains<T>(this, key);

    public bool RemoveValue(immutable(T)* key)
        => Standard::Collections::RemoveValue<T>(this, key);
}
