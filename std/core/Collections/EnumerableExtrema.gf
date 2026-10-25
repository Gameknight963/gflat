using std::Collections;
namespace std;

public T Min<T, S, E>(this Enumerable<T, S, E> source,
    int(immutable(T)*, immutable(T)*)* compare)
{
    E iterator = source.GetEnumerator();
    if (!iterator.MoveNext()) throw new Exception("Sequence is empty");
    T best = iterator.Current;
    while (iterator.MoveNext())
    {
        T current = iterator.Current;
        if (compare(&current, &best) < 0) best = move current;
    }
    return move best;
}

public T Min<T : IComparable<T>, S, E>(this Enumerable<T, S, E> source)
    => std::Min<T, S, E>(move source, Comparer<T>::Compare);

public T Max<T, S, E>(this Enumerable<T, S, E> source,
    int(immutable(T)*, immutable(T)*)* compare)
{
    E iterator = source.GetEnumerator();
    if (!iterator.MoveNext()) throw new Exception("Sequence is empty");
    T best = iterator.Current;
    while (iterator.MoveNext())
    {
        T current = iterator.Current;
        if (compare(&current, &best) > 0) best = move current;
    }
    return move best;
}

public T Max<T : IComparable<T>, S, E>(this Enumerable<T, S, E> source)
    => std::Max<T, S, E>(move source, Comparer<T>::Compare);
