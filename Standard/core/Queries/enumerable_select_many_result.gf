namespace Standard::Queries;

public Enumerable<R, SelectManyResultSource<T, U, R, S, E, IS, IE>, SelectManyResultSource<T, U, R, S, E, IS, IE>::Enumerator>
    SelectMany<T, U, R, S, E, IS, IE>(Enumerable<T, S, E> source,
        Enumerable<U, IS, IE>(T)* selector, R(T, U)* resultSelector)
    => source.SelectMany<U, R, IS, IE>(selector, resultSelector);

extension Enumerable<T, S, E>
{
    public Enumerable<R, SelectManyResultSource<T, U, R, S, E, IS, IE>, SelectManyResultSource<T, U, R, S, E, IS, IE>::Enumerator>
        SelectMany<U, R, IS, IE>(Enumerable<U, IS, IE>(T)* selector, R(T, U)* resultSelector)
        => new Enumerable<R, SelectManyResultSource<T, U, R, S, E, IS, IE>, SelectManyResultSource<T, U, R, S, E, IS, IE>::Enumerator>(
            new SelectManyResultSource<T, U, R, S, E, IS, IE>((*this).SourceView(), selector, resultSelector));
}
