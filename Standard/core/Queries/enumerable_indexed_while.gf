namespace Standard::Queries;

public Enumerable<T, IndexedTakeWhileSource<T, S, E>, IndexedTakeWhileSource<T, S, E>::Enumerator>
    TakeWhile<T, S, E>(Enumerable<T, S, E> source, bool(T, nuint)* predicate)
    => source.TakeWhile(predicate);

extension Enumerable<T, S, E>
{
    public Enumerable<T, IndexedTakeWhileSource<T, S, E>, IndexedTakeWhileSource<T, S, E>::Enumerator> TakeWhile(bool(T, nuint)* predicate)
        => new Enumerable<T, IndexedTakeWhileSource<T, S, E>, IndexedTakeWhileSource<T, S, E>::Enumerator>(
            new IndexedTakeWhileSource<T, S, E>((*this).SourceView(), predicate));
}

public Enumerable<T, IndexedSkipWhileSource<T, S, E>, IndexedSkipWhileSource<T, S, E>::Enumerator>
    SkipWhile<T, S, E>(Enumerable<T, S, E> source, bool(T, nuint)* predicate)
    => source.SkipWhile(predicate);

extension Enumerable<T, S, E>
{
    public Enumerable<T, IndexedSkipWhileSource<T, S, E>, IndexedSkipWhileSource<T, S, E>::Enumerator> SkipWhile(bool(T, nuint)* predicate)
        => new Enumerable<T, IndexedSkipWhileSource<T, S, E>, IndexedSkipWhileSource<T, S, E>::Enumerator>(
            new IndexedSkipWhileSource<T, S, E>((*this).SourceView(), predicate));
}
