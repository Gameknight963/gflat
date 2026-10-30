namespace std::Queries;

public Enumerable<T, IndexedWhereSource<T, S, E>, IndexedWhereSource<T, S, E>::Enumerator>
    Where<T, S, E>(Enumerable<T, S, E> source, bool(T, nuint)* predicate)
    => source.Where(predicate);

extension<T, S, E> Enumerable<T, S, E>
{
    public Enumerable<T, IndexedWhereSource<T, S, E>, IndexedWhereSource<T, S, E>::Enumerator> Where(bool(T, nuint)* predicate)
    => new Enumerable<T, IndexedWhereSource<T, S, E>, IndexedWhereSource<T, S, E>::Enumerator>(
            new IndexedWhereSource<T, S, E>((*this).SourceView(), predicate));
}
