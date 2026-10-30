using Standard::Collections;
namespace Standard::Queries;

public T Min<T, S, E>(Enumerable<T, S, E> source,
    int(immutable(T)*, immutable(T)*)* compare)
=> source.Min(compare);

extension<T, S, E> Enumerable<T, S, E>
{
    public T Min(int(immutable(T)*, immutable(T)*)* compare)
    {
        E iterator = (*this).GetEnumerator();
        if (!iterator.MoveNext()) throw new Exception("Sequence is empty");
        T best = iterator.Current;
        while (iterator.MoveNext())
        {
            T current = iterator.Current;
            if (compare(&current, &best) < 0) best = move current;
        }
        return move best;
    }
}

public T Min<T : IComparable<T>, S, E>(Enumerable<T, S, E> source)
    => source.Min();

extension<T : IComparable<T>, S, E> Enumerable<T, S, E>
{
    public T Min()
    => this.Min( Comparer<T>::Compare);
}

public T Max<T, S, E>(Enumerable<T, S, E> source,
    int(immutable(T)*, immutable(T)*)* compare)
=> source.Max(compare);

extension<T, S, E> Enumerable<T, S, E>
{
    public T Max(int(immutable(T)*, immutable(T)*)* compare)
    {
        E iterator = (*this).GetEnumerator();
        if (!iterator.MoveNext()) throw new Exception("Sequence is empty");
        T best = iterator.Current;
        while (iterator.MoveNext())
        {
            T current = iterator.Current;
            if (compare(&current, &best) > 0) best = move current;
        }
        return move best;
    }
}

public T Max<T : IComparable<T>, S, E>(Enumerable<T, S, E> source)
    => source.Max();

extension<T : IComparable<T>, S, E> Enumerable<T, S, E>
{
    public T Max()
    => this.Max( Comparer<T>::Compare);
}
