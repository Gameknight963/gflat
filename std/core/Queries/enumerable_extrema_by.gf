using std::Collections;
namespace std::Queries;

// Keep each projected key with its original element; compare cached keys only.
public T MinBy<T, K, S, E>(Enumerable<T, S, E> source, K(T)* selector,
    int(immutable(K)*, immutable(K)*)* compare)
=> source.MinBy<K>(selector, compare);

extension<T, S, E> Enumerable<T, S, E>
{
    public T MinBy<K>(K(T)* selector, int(immutable(K)*, immutable(K)*)* compare)
    {
        E iterator = (*this).GetEnumerator();
        if (!iterator.MoveNext()) throw new Exception("Sequence is empty");
        T best = iterator.Current;
        K bestKey = selector(best);
        while (iterator.MoveNext())
        {
            T current = iterator.Current;
            K key = selector(current);
            if (compare(&key, &bestKey) < 0)
            {
                best = move current;
                bestKey = move key;
            }
        }
        return move best;
    }
}

public T MinBy<T, K : IComparable<K>, S, E>(Enumerable<T, S, E> source, K(T)* selector)
    => source.MinBy<K>(selector);

extension<T, S, E> Enumerable<T, S, E>
{
    public T MinBy<K : IComparable<K>>(K(T)* selector)
    => this.MinBy<K>( selector, Comparer<K>::Compare);
}

public T MaxBy<T, K, S, E>(Enumerable<T, S, E> source, K(T)* selector,
    int(immutable(K)*, immutable(K)*)* compare)
=> source.MaxBy<K>(selector, compare);

extension<T, S, E> Enumerable<T, S, E>
{
    public T MaxBy<K>(K(T)* selector, int(immutable(K)*, immutable(K)*)* compare)
    {
        E iterator = (*this).GetEnumerator();
        if (!iterator.MoveNext()) throw new Exception("Sequence is empty");
        T best = iterator.Current;
        K bestKey = selector(best);
        while (iterator.MoveNext())
        {
            T current = iterator.Current;
            K key = selector(current);
            if (compare(&key, &bestKey) > 0)
            {
                best = move current;
                bestKey = move key;
            }
        }
        return move best;
    }
}

public T MaxBy<T, K : IComparable<K>, S, E>(Enumerable<T, S, E> source, K(T)* selector)
    => source.MaxBy<K>(selector);

extension<T, S, E> Enumerable<T, S, E>
{
    public T MaxBy<K : IComparable<K>>(K(T)* selector)
    => this.MaxBy<K>( selector, Comparer<K>::Compare);
}


// Empty input leaves result unchanged; commit only after the complete scan succeeds.
public bool TryMinBy<T, K, S, E>(Enumerable<T, S, E> source, K(T)* selector,
    int(immutable(K)*, immutable(K)*)* compare, ref T result)
=> source.TryMinBy<K>(selector, compare, ref result);

extension<T, S, E> Enumerable<T, S, E>
{
    public bool TryMinBy<K>(K(T)* selector, int(immutable(K)*, immutable(K)*)* compare, ref T result)
    {
        E iterator = (*this).GetEnumerator();
        if (!iterator.MoveNext()) return false;
        T best = iterator.Current;
        K bestKey = selector(best);
        while (iterator.MoveNext())
        {
            T current = iterator.Current;
            K key = selector(current);
            if (compare(&key, &bestKey) < 0)
            {
                best = move current;
                bestKey = move key;
            }
        }
        result = move best;
        return true;
    }
}

public bool TryMinBy<T, K : IComparable<K>, S, E>(Enumerable<T, S, E> source,
    K(T)* selector, ref T result)
    => source.TryMinBy<K>(selector, ref result);

extension<T, S, E> Enumerable<T, S, E>
{
    public bool TryMinBy<K : IComparable<K>>(K(T)* selector, ref T result)
    => this.TryMinBy<K>( selector, Comparer<K>::Compare, ref result);
}

// Empty input leaves result unchanged; commit only after the complete scan succeeds.
public bool TryMaxBy<T, K, S, E>(Enumerable<T, S, E> source, K(T)* selector,
    int(immutable(K)*, immutable(K)*)* compare, ref T result)
=> source.TryMaxBy<K>(selector, compare, ref result);

extension<T, S, E> Enumerable<T, S, E>
{
    public bool TryMaxBy<K>(K(T)* selector, int(immutable(K)*, immutable(K)*)* compare, ref T result)
    {
        E iterator = (*this).GetEnumerator();
        if (!iterator.MoveNext()) return false;
        T best = iterator.Current;
        K bestKey = selector(best);
        while (iterator.MoveNext())
        {
            T current = iterator.Current;
            K key = selector(current);
            if (compare(&key, &bestKey) > 0)
            {
                best = move current;
                bestKey = move key;
            }
        }
        result = move best;
        return true;
    }
}

public bool TryMaxBy<T, K : IComparable<K>, S, E>(Enumerable<T, S, E> source,
    K(T)* selector, ref T result)
    => source.TryMaxBy<K>(selector, ref result);

extension<T, S, E> Enumerable<T, S, E>
{
    public bool TryMaxBy<K : IComparable<K>>(K(T)* selector, ref T result)
    => this.TryMaxBy<K>( selector, Comparer<K>::Compare, ref result);
}
