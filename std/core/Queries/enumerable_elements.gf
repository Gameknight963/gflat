using std::Collections;
namespace std::Queries;

public T First<T, S, E>(Enumerable<T, S, E> source)
=> source.First();

extension<T, S, E> Enumerable<T, S, E>
{
    public T First()
    {
        E iterator = (*this).GetEnumerator();
        if (!iterator.MoveNext()) throw new Exception("Sequence is empty");
        return iterator.Current;
    }
}

public T Last<T, S, E>(Enumerable<T, S, E> source)
=> source.Last();

extension<T, S, E> Enumerable<T, S, E>
{
    public T Last()
    {
        E iterator = (*this).GetEnumerator();
        if (!iterator.MoveNext()) throw new Exception("Sequence is empty");
        while (true)
        {
            T result = iterator.Current;
            if (!iterator.MoveNext()) return result;
        }
    }
}

public T Single<T, S, E>(Enumerable<T, S, E> source)
=> source.Single();

extension<T, S, E> Enumerable<T, S, E>
{
    public T Single()
    {
        E iterator = (*this).GetEnumerator();
        if (!iterator.MoveNext()) throw new Exception("Sequence is empty");
        T result = iterator.Current;
        if (iterator.MoveNext()) throw new Exception("Sequence contains more than one element");
        return result;
    }
}

// Leaves result unchanged when the sequence is empty.
public bool TryFirst<T, S, E>(Enumerable<T, S, E> source, ref T result)
=> source.TryFirst(ref result);

extension<T, S, E> Enumerable<T, S, E>
{
    public bool TryFirst(ref T result)
    {
        E iterator = (*this).GetEnumerator();
        if (!iterator.MoveNext()) return false;
        result = iterator.Current;
        return true;
    }
}

// Do not replace result until every element and the final MoveNext succeeded.
public bool TryLast<T, S, E>(Enumerable<T, S, E> source, ref T result)
=> source.TryLast(ref result);

extension<T, S, E> Enumerable<T, S, E>
{
    public bool TryLast(ref T result)
    {
        E iterator = (*this).GetEnumerator();
        if (!iterator.MoveNext()) return false;
        while (true)
        {
            T last = iterator.Current;
            if (!iterator.MoveNext()) { result = move last; return true; }
        }
    }
}

// Empty and multiple-element sequences both leave result unchanged.
public bool TrySingle<T, S, E>(Enumerable<T, S, E> source, ref T result)
=> source.TrySingle(ref result);

extension<T, S, E> Enumerable<T, S, E>
{
    public bool TrySingle(ref T result)
    {
        E iterator = (*this).GetEnumerator();
        if (!iterator.MoveNext()) return false;
        T single = iterator.Current;
        if (iterator.MoveNext()) return false;
        result = move single;
        return true;
    }
}

