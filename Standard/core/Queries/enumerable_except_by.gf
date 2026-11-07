namespace Standard::Queries;

public Enumerable<T, SetFilterBySource<T, K, LS, LE, RS, RE>, SetFilterBySource<T, K, LS, LE, RS, RE>::Enumerator>
    ExceptBy<T, K : IHashable, LS, LE, RS, RE>(Enumerable<T, LS, LE> first, Enumerable<K, RS, RE> second, K(T)* selector)
    => first.ExceptBy<K, RS, RE>(move second, selector);

extension Enumerable<T, LS, LE>
{
    public Enumerable<T, SetFilterBySource<T, K, LS, LE, RS, RE>, SetFilterBySource<T, K, LS, LE, RS, RE>::Enumerator>
        ExceptBy<K : IHashable, RS, RE>(Enumerable<K, RS, RE> second, K(T)* selector)
        => new Enumerable<T, SetFilterBySource<T, K, LS, LE, RS, RE>, SetFilterBySource<T, K, LS, LE, RS, RE>::Enumerator>(
            new SetFilterBySource<T, K, LS, LE, RS, RE>((*this).SourceView(), second.SourceView(), selector, false));
}
