namespace Standard::Queries;

public Enumerable<T, ConcatSource<T, LS, LE, RS, RE>, ConcatSource<T, LS, LE, RS, RE>::Enumerator>
    Concat<T, LS, LE, RS, RE>(Enumerable<T, LS, LE> first, Enumerable<T, RS, RE> second)
    => first.Concat<RS, RE>(second);

extension<T, LS, LE> Enumerable<T, LS, LE>
{
    public Enumerable<T, ConcatSource<T, LS, LE, RS, RE>, ConcatSource<T, LS, LE, RS, RE>::Enumerator> Concat<RS, RE>(Enumerable<T, RS, RE> second)
    => new Enumerable<T, ConcatSource<T, LS, LE, RS, RE>, ConcatSource<T, LS, LE, RS, RE>::Enumerator>(
            new ConcatSource<T, LS, LE, RS, RE>((*this).SourceView(), second.SourceView()));
}
