namespace Standard::Queries;

public Enumerable<T, UnionSource<T, LS, LE, RS, RE>, UnionSource<T, LS, LE, RS, RE>::Enumerator>
    Union<T : IHashable, LS, LE, RS, RE>(Enumerable<T, LS, LE> first, Enumerable<T, RS, RE> second)
    => first.Union<RS, RE>(move second);

extension Enumerable<T : IHashable, LS, LE>
{
    public Enumerable<T, UnionSource<T, LS, LE, RS, RE>, UnionSource<T, LS, LE, RS, RE>::Enumerator>
        Union<RS, RE>(Enumerable<T, RS, RE> second)
        => new Enumerable<T, UnionSource<T, LS, LE, RS, RE>, UnionSource<T, LS, LE, RS, RE>::Enumerator>(
            new UnionSource<T, LS, LE, RS, RE>((*this).SourceView(), second.SourceView()));
}
