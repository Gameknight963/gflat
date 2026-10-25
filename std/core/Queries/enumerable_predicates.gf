using std::Collections;
namespace std::Queries;

public bool Any<T, S, E>(this Enumerable<T, S, E> source)
{
    E iterator = source.GetEnumerator();
    return iterator.MoveNext();
}

public bool Any<T, S, E>(this Enumerable<T, S, E> source, bool(T)* predicate)
{
    E iterator = source.GetEnumerator();
    while (iterator.MoveNext()) if (predicate(iterator.Current)) return true;
    return false;
}

public bool All<T, S, E>(this Enumerable<T, S, E> source, bool(T)* predicate)
{
    E iterator = source.GetEnumerator();
    while (iterator.MoveNext()) if (!predicate(iterator.Current)) return false;
    return true;
}

