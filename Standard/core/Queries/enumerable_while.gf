using Standard::Collections;
namespace Standard::Queries;

public Enumerable<T, TakeWhileSource<T, S, E>, TakeWhileSource<T, S, E>::Enumerator> TakeWhile<T, S, E>(Enumerable<T, S, E> source, bool(T)* predicate)
    => source.TakeWhile(predicate);

extension<T, S, E> Enumerable<T, S, E>
{
    public Enumerable<T, TakeWhileSource<T, S, E>, TakeWhileSource<T, S, E>::Enumerator> TakeWhile(bool(T)* predicate)
    => new Enumerable<T, TakeWhileSource<T, S, E>, TakeWhileSource<T, S, E>::Enumerator>(new TakeWhileSource<T, S, E>((*this).SourceView(), predicate));
}

public Enumerable<T, SkipWhileSource<T, S, E>, SkipWhileSource<T, S, E>::Enumerator> SkipWhile<T, S, E>(Enumerable<T, S, E> source, bool(T)* predicate)
    => source.SkipWhile(predicate);

extension<T, S, E> Enumerable<T, S, E>
{
    public Enumerable<T, SkipWhileSource<T, S, E>, SkipWhileSource<T, S, E>::Enumerator> SkipWhile(bool(T)* predicate)
    => new Enumerable<T, SkipWhileSource<T, S, E>, SkipWhileSource<T, S, E>::Enumerator>(new SkipWhileSource<T, S, E>((*this).SourceView(), predicate));
}
