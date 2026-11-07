namespace Standard::Queries;

public Enumerable<U, IndexedSelectManySource<T, U, S, E, IS, IE>, IndexedSelectManySource<T, U, S, E, IS, IE>::Enumerator>
    SelectMany<T, U, S, E, IS, IE>(Enumerable<T, S, E> source, Enumerable<U, IS, IE>(T, nuint)* selector)
    => source.SelectMany<U, IS, IE>(selector);

extension Enumerable<T, S, E>
{
    public Enumerable<U, IndexedSelectManySource<T, U, S, E, IS, IE>, IndexedSelectManySource<T, U, S, E, IS, IE>::Enumerator>
        SelectMany<U, IS, IE>(Enumerable<U, IS, IE>(T, nuint)* selector)
        => new Enumerable<U, IndexedSelectManySource<T, U, S, E, IS, IE>, IndexedSelectManySource<T, U, S, E, IS, IE>::Enumerator>(
            new IndexedSelectManySource<T, U, S, E, IS, IE>((*this).SourceView(), selector));
}
