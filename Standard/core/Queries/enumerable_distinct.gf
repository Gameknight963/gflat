namespace Standard::Queries;

public Enumerable<T, DistinctSource<T, S, E>, DistinctSource<T, S, E>::Enumerator>
    Distinct<T : IHashable, S, E>(Enumerable<T, S, E> source)
    => source.Distinct();

extension Enumerable<T : IHashable, S, E>
{
    public Enumerable<T, DistinctSource<T, S, E>, DistinctSource<T, S, E>::Enumerator> Distinct()
    => new Enumerable<T, DistinctSource<T, S, E>, DistinctSource<T, S, E>::Enumerator>(
            new DistinctSource<T, S, E>((*this).SourceView()));
}
