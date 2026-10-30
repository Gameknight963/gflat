namespace Standard::Queries;

public Enumerable<U, IndexedSelectSource<T, U, S, E>, IndexedSelectSource<T, U, S, E>::Enumerator>
    Select<T, U, S, E>(Enumerable<T, S, E> source, U(T, nuint)* selector)
    => source.Select<U>(selector);

extension<T, S, E> Enumerable<T, S, E>
{
    public Enumerable<U, IndexedSelectSource<T, U, S, E>, IndexedSelectSource<T, U, S, E>::Enumerator> Select<U>(U(T, nuint)* selector)
    => new Enumerable<U, IndexedSelectSource<T, U, S, E>, IndexedSelectSource<T, U, S, E>::Enumerator>(
            new IndexedSelectSource<T, U, S, E>((*this).SourceView(), selector));
}
