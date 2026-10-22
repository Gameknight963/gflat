namespace std;

// Separate copying operation: ordinary HashSet<T> operations still accept
// noncopyable keys without instantiating this helper.
public void UnionWith<T : IHashable>(this ref HashSet<T> target, immutable(HashSet<T>)* other)
{
    if ((nuint)&target == (nuint)other) return;
    HashSet<T>::Enumerator iterator = other.GetEnumerator();
    while (iterator.MoveNext())
    {
        immutable(T)* key = iterator.Current;
        if (!target.Contains(key)) target.Add(*key);
    }
}

// Remove shared values; copying is required only for values absent from target.
public void SymmetricExceptWith<T : IHashable>(this ref HashSet<T> target, immutable(HashSet<T>)* other)
{
    if ((nuint)&target == (nuint)other) { target.Clear(); return; }
    HashSet<T>::Enumerator iterator = other.GetEnumerator();
    while (iterator.MoveNext())
    {
        immutable(T)* key = iterator.Current;
        if (!target.Remove(key)) target.Add(*key);
    }
}
