namespace std;

public Enumerable<T, IndexedWhereSource<T, S, E>, IndexedWhereSource<T, S, E>::Enumerator>
    Where<T, S, E>(this Enumerable<T, S, E> source, bool(T, nuint)* predicate)
    => new Enumerable<T, IndexedWhereSource<T, S, E>, IndexedWhereSource<T, S, E>::Enumerator>(
        new IndexedWhereSource<T, S, E>(source.SourceView(), predicate));
