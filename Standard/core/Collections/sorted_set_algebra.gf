namespace Standard::Collections;

public void UnionWith<T>(ref SortedSet<T> target, immutable(SortedSet<T>)* other)
{
    if ((&target).reinterpret<nuint>() == (other).reinterpret<nuint>()) return;
    SortedSet<T>::Enumerator iterator = other.GetEnumerator();
    while (iterator.MoveNext())
    {
        immutable(T)* key = iterator.Current;
        if (!target.Contains(key)) target.Add(*key);
    }
}

public void SymmetricExceptWith<T>(ref SortedSet<T> target, immutable(SortedSet<T>)* other)
{
    if ((&target).reinterpret<nuint>() == (other).reinterpret<nuint>())
    {
        target.Clear();
        return;
    }
    SortedSet<T>::Enumerator iterator = other.GetEnumerator();
    while (iterator.MoveNext())
    {
        immutable(T)* key = iterator.Current;
        if (target.HasEquivalentBefore(other, key)) continue;
        if (!target.Remove(key)) target.Add(*key);
    }
}

extension SortedSet<T>
{
    public void UnionWith(immutable(SortedSet<T>)* other)
        => Standard::Collections::UnionWith<T>(ref *this, other);

    public void SymmetricExceptWith(immutable(SortedSet<T>)* other)
        => Standard::Collections::SymmetricExceptWith<T>(ref *this, other);
}
