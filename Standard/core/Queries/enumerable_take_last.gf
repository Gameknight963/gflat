namespace Standard::Queries;

public Enumerable<T, TakeLastSource<T, S, E>, TakeLastSource<T, S, E>::Enumerator>
    TakeLast<T, S, E>(Enumerable<T, S, E> source, nuint count)
    => source.TakeLast(count);

extension<T, S, E> Enumerable<T, S, E>
{
    public Enumerable<T, TakeLastSource<T, S, E>, TakeLastSource<T, S, E>::Enumerator> TakeLast(nuint count)
    => new Enumerable<T, TakeLastSource<T, S, E>, TakeLastSource<T, S, E>::Enumerator>(
            new TakeLastSource<T, S, E>((*this).SourceView(), count));
}
