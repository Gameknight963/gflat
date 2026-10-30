using Standard::Collections;
namespace Standard::Queries;

public Enumerable<T, WhereSource<T, S, E>, WhereSource<T, S, E>::Enumerator> Where<T, S, E>(Enumerable<T, S, E> source, bool(T)* predicate)
    => source.Where(predicate);

extension<T, S, E> Enumerable<T, S, E>
{
    public Enumerable<T, WhereSource<T, S, E>, WhereSource<T, S, E>::Enumerator> Where(bool(T)* predicate)
    => new Enumerable<T, WhereSource<T, S, E>, WhereSource<T, S, E>::Enumerator>(new WhereSource<T, S, E>((*this).SourceView(), predicate));
}

