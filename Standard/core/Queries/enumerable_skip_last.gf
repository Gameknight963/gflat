namespace Standard::Queries;

public Enumerable<T, SkipLastSource<T, S, E>, SkipLastSource<T, S, E>::Enumerator>
    SkipLast<T, S, E>(Enumerable<T, S, E> source, nuint count)
    => source.SkipLast(count);

extension<T, S, E> Enumerable<T, S, E>
{
    public Enumerable<T, SkipLastSource<T, S, E>, SkipLastSource<T, S, E>::Enumerator> SkipLast(nuint count)
    => new Enumerable<T, SkipLastSource<T, S, E>, SkipLastSource<T, S, E>::Enumerator>(
            new SkipLastSource<T, S, E>((*this).SourceView(), count));
}
