using std::Collections;
namespace std;

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
    => std::MinBy<T, K, S, E>(move source, selector, Comparer<K>::Compare);

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
    => std::MaxBy<T, K, S, E>(move source, selector, Comparer<K>::Compare);
