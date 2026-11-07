namespace Standard::Queries;

public T First<T, S, E>(Enumerable<T, S, E> source, bool(T)* predicate)
    => source.First(predicate);

extension Enumerable<T, S, E>
{
    public T First(bool(T)* predicate)
    {
        E iterator = (*this).GetEnumerator();
        while (iterator.MoveNext())
        {
            T current = iterator.Current;
            if (predicate(current)) return move current;
        }
        throw new Exception("Sequence contains no matching element");
    }
}

public T Last<T, S, E>(Enumerable<T, S, E> source, bool(T)* predicate)
    => source.Last(predicate);

extension Enumerable<T, S, E>
{
    public T Last(bool(T)* predicate)
    {
        E iterator = (*this).GetEnumerator();
        while (iterator.MoveNext())
        {
            T result = iterator.Current;
            if (!predicate(result)) continue;
            while (iterator.MoveNext())
            {
                T current = iterator.Current;
                if (predicate(current)) result = move current;
            }
            return move result;
        }
        throw new Exception("Sequence contains no matching element");
    }
}

public T Single<T, S, E>(Enumerable<T, S, E> source, bool(T)* predicate)
    => source.Single(predicate);

extension Enumerable<T, S, E>
{
    public T Single(bool(T)* predicate)
    {
        E iterator = (*this).GetEnumerator();
        while (iterator.MoveNext())
        {
            T result = iterator.Current;
            if (!predicate(result)) continue;
            while (iterator.MoveNext())
                if (predicate(iterator.Current)) throw new Exception("Sequence contains more than one matching element");
            return move result;
        }
        throw new Exception("Sequence contains no matching element");
    }
}

public bool TryFirst<T, S, E>(Enumerable<T, S, E> source, bool(T)* predicate, ref T result)
    => source.TryFirst(predicate, ref result);

extension Enumerable<T, S, E>
{
    public bool TryFirst(bool(T)* predicate, ref T result)
    {
        E iterator = (*this).GetEnumerator();
        while (iterator.MoveNext())
        {
            T current = iterator.Current;
            if (predicate(current)) { result = move current; return true; }
        }
        return false;
    }
}

public bool TryLast<T, S, E>(Enumerable<T, S, E> source, bool(T)* predicate, ref T result)
    => source.TryLast(predicate, ref result);

extension Enumerable<T, S, E>
{
    public bool TryLast(bool(T)* predicate, ref T result)
    {
        E iterator = (*this).GetEnumerator();
        while (iterator.MoveNext())
        {
            T last = iterator.Current;
            if (!predicate(last)) continue;
            while (iterator.MoveNext())
            {
                T current = iterator.Current;
                if (predicate(current)) last = move current;
            }
            result = move last;
            return true;
        }
        return false;
    }
}

public bool TrySingle<T, S, E>(Enumerable<T, S, E> source, bool(T)* predicate, ref T result)
    => source.TrySingle(predicate, ref result);

extension Enumerable<T, S, E>
{
    public bool TrySingle(bool(T)* predicate, ref T result)
    {
        E iterator = (*this).GetEnumerator();
        while (iterator.MoveNext())
        {
            T single = iterator.Current;
            if (!predicate(single)) continue;
            while (iterator.MoveNext())
                if (predicate(iterator.Current)) return false;
            result = move single;
            return true;
        }
        return false;
    }
}
