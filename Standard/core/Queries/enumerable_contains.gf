using Standard::Collections;
namespace Standard::Queries;

// The searched key is borrowed. Enumeration still follows ordinary T semantics:
// materialize Current once and lend only this iteration's local to the comparer.
public bool Contains<T, S, E>(Enumerable<T, S, E> source, immutable(T)* key,
    bool(immutable(T)*, immutable(T)*)* equal)
=> source.Contains(key, equal);

extension<T, S, E> Enumerable<T, S, E>
{
    public bool Contains(immutable(T)* key, bool(immutable(T)*, immutable(T)*)* equal)
    {
        E iterator = (*this).GetEnumerator();
        while (iterator.MoveNext())
        {
            T current = iterator.Current;
            if (equal(&current, key)) return true;
        }
        return false;
    }
}

public bool Contains<T : IEquatable<T>, S, E>(Enumerable<T, S, E> source, immutable(T)* key)
    => source.Contains(key);

extension<T : IEquatable<T>, S, E> Enumerable<T, S, E>
{
    public bool Contains(immutable(T)* key)
    => this.Contains( key, EqualityComparer<T>::Equals);
}
