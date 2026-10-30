using Standard::Collections;
namespace Standard::Queries;

public Enumerable<U, SelectSource<T, U, S, E>, SelectSource<T, U, S, E>::Enumerator> Select<T, U, S, E>(Enumerable<T, S, E> source, U(T)* selector)
    => source.Select<U>(selector);

extension<T, S, E> Enumerable<T, S, E>
{
    public Enumerable<U, SelectSource<T, U, S, E>, SelectSource<T, U, S, E>::Enumerator> Select<U>(U(T)* selector)
    => new Enumerable<U, SelectSource<T, U, S, E>, SelectSource<T, U, S, E>::Enumerator>(new SelectSource<T, U, S, E>((*this).SourceView(), selector));
}

