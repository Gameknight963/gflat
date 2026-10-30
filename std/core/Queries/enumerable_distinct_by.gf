namespace std::Queries;

public Enumerable<T, DistinctBySource<T, K, S, E>, DistinctBySource<T, K, S, E>::Enumerator>
    DistinctBy<T, K : IHashable, S, E>(Enumerable<T, S, E> source, K(T)* selector)
    => source.DistinctBy<K>(selector);

extension<T, S, E> Enumerable<T, S, E>
{
    public Enumerable<T, DistinctBySource<T, K, S, E>, DistinctBySource<T, K, S, E>::Enumerator> DistinctBy<K : IHashable>(K(T)* selector)
    => new Enumerable<T, DistinctBySource<T, K, S, E>, DistinctBySource<T, K, S, E>::Enumerator>(
            new DistinctBySource<T, K, S, E>((*this).SourceView(), selector));
}
