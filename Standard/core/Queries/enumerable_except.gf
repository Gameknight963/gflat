namespace Standard::Queries;

public Enumerable<T, SetFilterSource<T, LS, LE, RS, RE>, SetFilterSource<T, LS, LE, RS, RE>::Enumerator>
    Except<T : IHashable, LS, LE, RS, RE>(Enumerable<T, LS, LE> first, Enumerable<T, RS, RE> second)
    => first.Except<RS, RE>(move second);

extension Enumerable<T : IHashable, LS, LE>
{
    public Enumerable<T, SetFilterSource<T, LS, LE, RS, RE>, SetFilterSource<T, LS, LE, RS, RE>::Enumerator>
        Except<RS, RE>(Enumerable<T, RS, RE> second)
        => new Enumerable<T, SetFilterSource<T, LS, LE, RS, RE>, SetFilterSource<T, LS, LE, RS, RE>::Enumerator>(
            new SetFilterSource<T, LS, LE, RS, RE>((*this).SourceView(), second.SourceView(), false));
}
