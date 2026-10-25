namespace std;

public Enumerable<T, DefaultIfEmptySource<T, S, E>, DefaultIfEmptySource<T, S, E>::Enumerator>
    DefaultIfEmpty<T, S, E>(this Enumerable<T, S, E> source, T value)
    => new Enumerable<T, DefaultIfEmptySource<T, S, E>, DefaultIfEmptySource<T, S, E>::Enumerator>(
        new DefaultIfEmptySource<T, S, E>(source.SourceView(), move value));
