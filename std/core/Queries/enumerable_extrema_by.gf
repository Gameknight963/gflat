using std::Collections;
namespace std::Queries;

// Keep each projected key with its original element; compare cached keys only.
public T MinBy<T, K, S, E>(this Enumerable<T, S, E> source, K(T)* selector,
    int(immutable(K)*, immutable(K)*)* compare)
{
    E iterator = source.GetEnumerator();
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

public T MinBy<T, K : IComparable<K>, S, E>(this Enumerable<T, S, E> source, K(T)* selector)
    => std::Queries::MinBy<T, K, S, E>(move source, selector, Comparer<K>::Compare);

public T MaxBy<T, K, S, E>(this Enumerable<T, S, E> source, K(T)* selector,
    int(immutable(K)*, immutable(K)*)* compare)
{
    E iterator = source.GetEnumerator();
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

public T MaxBy<T, K : IComparable<K>, S, E>(this Enumerable<T, S, E> source, K(T)* selector)
    => std::Queries::MaxBy<T, K, S, E>(move source, selector, Comparer<K>::Compare);


// Empty input leaves result unchanged; commit only after the complete scan succeeds.
public bool TryMinBy<T, K, S, E>(this Enumerable<T, S, E> source, K(T)* selector,
    int(immutable(K)*, immutable(K)*)* compare, ref T result)
{
    E iterator = source.GetEnumerator();
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

public bool TryMinBy<T, K : IComparable<K>, S, E>(this Enumerable<T, S, E> source,
    K(T)* selector, ref T result)
    => std::Queries::TryMinBy<T, K, S, E>(move source, selector, Comparer<K>::Compare, ref result);

// Empty input leaves result unchanged; commit only after the complete scan succeeds.
public bool TryMaxBy<T, K, S, E>(this Enumerable<T, S, E> source, K(T)* selector,
    int(immutable(K)*, immutable(K)*)* compare, ref T result)
{
    E iterator = source.GetEnumerator();
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

public bool TryMaxBy<T, K : IComparable<K>, S, E>(this Enumerable<T, S, E> source,
    K(T)* selector, ref T result)
    => std::Queries::TryMaxBy<T, K, S, E>(move source, selector, Comparer<K>::Compare, ref result);
