using std::Collections;
namespace std::Queries;

public Enumerable<T, TakeWhileSource<T, S, E>, TakeWhileSource<T, S, E>::Enumerator> TakeWhile<T, S, E>(this Enumerable<T, S, E> source, bool(T)* predicate)
    => new Enumerable<T, TakeWhileSource<T, S, E>, TakeWhileSource<T, S, E>::Enumerator>(new TakeWhileSource<T, S, E>(source.SourceView(), predicate));

public Enumerable<T, SkipWhileSource<T, S, E>, SkipWhileSource<T, S, E>::Enumerator> SkipWhile<T, S, E>(this Enumerable<T, S, E> source, bool(T)* predicate)
    => new Enumerable<T, SkipWhileSource<T, S, E>, SkipWhileSource<T, S, E>::Enumerator>(new SkipWhileSource<T, S, E>(source.SourceView(), predicate));
