namespace std::Collections;

// Separate copying operation: ordinary HashSet<T> operations still accept
// noncopyable keys without instantiating this helper.
public void UnionWith<T : IHashable>(ref HashSet<T> target, immutable(HashSet<T>)* other)
{
    if ((&target).reinterpret<nuint>() == (other).reinterpret<nuint>()) return;
    HashSet<T>::Enumerator iterator = other.GetEnumerator();
    while (iterator.MoveNext())
    {
        immutable(T)* key = iterator.Current;
        if (!target.Contains(key)) target.Add(*key);
    }
}

extension<T : IHashable> HashSet<T>
{
    public void UnionWith(immutable(HashSet<T>)* other)
    => std::Collections::UnionWith<T>(ref *this, other);
}

// Remove shared values; copying is required only for values absent from target.
public void SymmetricExceptWith<T : IHashable>(ref HashSet<T> target, immutable(HashSet<T>)* other)
{
    if ((&target).reinterpret<nuint>() == (other).reinterpret<nuint>()) { target.Clear(); return; }
    HashSet<T>::Enumerator iterator = other.GetEnumerator();
    while (iterator.MoveNext())
    {
        immutable(T)* key = iterator.Current;
        if (!target.Remove(key)) target.Add(*key);
    }
}

extension<T : IHashable> HashSet<T>
{
    public void SymmetricExceptWith(immutable(HashSet<T>)* other)
    => std::Collections::SymmetricExceptWith<T>(ref *this, other);
}
