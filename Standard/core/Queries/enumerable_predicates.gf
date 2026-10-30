using Standard::Collections;
namespace Standard::Queries;

public bool Any<T, S, E>(Enumerable<T, S, E> source)
=> source.Any();

extension<T, S, E> Enumerable<T, S, E>
{
    public bool Any()
    {
        E iterator = (*this).GetEnumerator();
        return iterator.MoveNext();
    }
}

public bool Any<T, S, E>(Enumerable<T, S, E> source, bool(T)* predicate)
=> source.Any(predicate);

extension<T, S, E> Enumerable<T, S, E>
{
    public bool Any(bool(T)* predicate)
    {
        E iterator = (*this).GetEnumerator();
        while (iterator.MoveNext()) if (predicate(iterator.Current)) return true;
        return false;
    }
}

public bool All<T, S, E>(Enumerable<T, S, E> source, bool(T)* predicate)
=> source.All(predicate);

extension<T, S, E> Enumerable<T, S, E>
{
    public bool All(bool(T)* predicate)
    {
        E iterator = (*this).GetEnumerator();
        while (iterator.MoveNext()) if (!predicate(iterator.Current)) return false;
        return true;
    }
}

