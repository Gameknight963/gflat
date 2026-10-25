namespace std::Queries;

public Enumerable<T, DistinctBySource<T, K, S, E>, DistinctBySource<T, K, S, E>::Enumerator>
    DistinctBy<T, K : IHashable, S, E>(this Enumerable<T, S, E> source, K(T)* selector)
    => new Enumerable<T, DistinctBySource<T, K, S, E>, DistinctBySource<T, K, S, E>::Enumerator>(
        new DistinctBySource<T, K, S, E>(source.SourceView(), selector));
