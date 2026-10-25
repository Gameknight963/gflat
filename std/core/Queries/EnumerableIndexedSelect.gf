namespace std;

public Enumerable<U, IndexedSelectSource<T, U, S, E>, IndexedSelectSource<T, U, S, E>::Enumerator>
    Select<T, U, S, E>(this Enumerable<T, S, E> source, U(T, nuint)* selector)
    => new Enumerable<U, IndexedSelectSource<T, U, S, E>, IndexedSelectSource<T, U, S, E>::Enumerator>(
        new IndexedSelectSource<T, U, S, E>(source.SourceView(), selector));
