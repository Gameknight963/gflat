namespace Standard::Queries;

public Enumerable<T, UnionBySource<T, K, LS, LE, RS, RE>, UnionBySource<T, K, LS, LE, RS, RE>::Enumerator>
    UnionBy<T, K : IHashable, LS, LE, RS, RE>(Enumerable<T, LS, LE> first, Enumerable<T, RS, RE> second, K(T)* selector)
    => first.UnionBy<K, RS, RE>(move second, selector);

extension Enumerable<T, LS, LE>
{
    public Enumerable<T, UnionBySource<T, K, LS, LE, RS, RE>, UnionBySource<T, K, LS, LE, RS, RE>::Enumerator>
        UnionBy<K : IHashable, RS, RE>(Enumerable<T, RS, RE> second, K(T)* selector)
        => new Enumerable<T, UnionBySource<T, K, LS, LE, RS, RE>, UnionBySource<T, K, LS, LE, RS, RE>::Enumerator>(
            new UnionBySource<T, K, LS, LE, RS, RE>((*this).SourceView(), second.SourceView(), selector));
}
