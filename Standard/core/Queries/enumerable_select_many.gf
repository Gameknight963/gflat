namespace Standard::Queries;

public Enumerable<U, SelectManySource<T, U, S, E, IS, IE>, SelectManySource<T, U, S, E, IS, IE>::Enumerator>
    SelectMany<T, U, S, E, IS, IE>(Enumerable<T, S, E> source, Enumerable<U, IS, IE>(T)* selector)
    => source.SelectMany<U, IS, IE>(selector);

extension Enumerable<T, S, E>
{
    public Enumerable<U, SelectManySource<T, U, S, E, IS, IE>, SelectManySource<T, U, S, E, IS, IE>::Enumerator>
        SelectMany<U, IS, IE>(Enumerable<U, IS, IE>(T)* selector)
        => new Enumerable<U, SelectManySource<T, U, S, E, IS, IE>, SelectManySource<T, U, S, E, IS, IE>::Enumerator>(
            new SelectManySource<T, U, S, E, IS, IE>((*this).SourceView(), selector));
}
