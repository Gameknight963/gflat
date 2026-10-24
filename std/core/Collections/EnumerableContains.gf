namespace std;

// The searched key is borrowed. Enumeration still follows ordinary T semantics:
// materialize Current once and lend only this iteration's local to the comparer.
public bool Contains<T, S, E>(this Enumerable<T, S, E> source, immutable(T)* key,
    bool(immutable(T)*, immutable(T)*)* equal)
{
    E iterator = source.GetEnumerator();
    while (iterator.MoveNext())
    {
        T current = iterator.Current;
        if (equal(&current, key)) return true;
    }
    return false;
}

public bool Contains<T : IEquatable<T>, S, E>(this Enumerable<T, S, E> source, immutable(T)* key)
    => std::Contains<T, S, E>(move source, key, EqualityComparer<T>::Equals);
