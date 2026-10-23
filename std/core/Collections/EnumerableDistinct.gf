namespace std;

public Enumerable<T, DistinctSource<T, S, E>, DistinctSource<T, S, E>::Enumerator>
    Distinct<T : IHashable, S, E>(this Enumerable<T, S, E> source)
    => new Enumerable<T, DistinctSource<T, S, E>, DistinctSource<T, S, E>::Enumerator>(
        new DistinctSource<T, S, E>(source.SourceView()));
