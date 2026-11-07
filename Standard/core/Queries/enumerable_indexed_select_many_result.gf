namespace Standard::Queries;

public Enumerable<R, IndexedSelectManyResultSource<T, U, R, S, E, IS, IE>, IndexedSelectManyResultSource<T, U, R, S, E, IS, IE>::Enumerator>
    SelectMany<T, U, R, S, E, IS, IE>(Enumerable<T, S, E> source,
        Enumerable<U, IS, IE>(T, nuint)* selector, R(T, U)* resultSelector)
    => source.SelectMany<U, R, IS, IE>(selector, resultSelector);

extension Enumerable<T, S, E>
{
    public Enumerable<R, IndexedSelectManyResultSource<T, U, R, S, E, IS, IE>, IndexedSelectManyResultSource<T, U, R, S, E, IS, IE>::Enumerator>
        SelectMany<U, R, IS, IE>(Enumerable<U, IS, IE>(T, nuint)* selector, R(T, U)* resultSelector)
        => new Enumerable<R, IndexedSelectManyResultSource<T, U, R, S, E, IS, IE>, IndexedSelectManyResultSource<T, U, R, S, E, IS, IE>::Enumerator>(
            new IndexedSelectManyResultSource<T, U, R, S, E, IS, IE>((*this).SourceView(), selector, resultSelector));
}
