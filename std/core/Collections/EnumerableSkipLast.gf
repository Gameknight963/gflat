namespace std;

public Enumerable<T, SkipLastSource<T, S, E>, SkipLastSource<T, S, E>::Enumerator>
    SkipLast<T, S, E>(this Enumerable<T, S, E> source, nuint count)
    => new Enumerable<T, SkipLastSource<T, S, E>, SkipLastSource<T, S, E>::Enumerator>(
        new SkipLastSource<T, S, E>(source.SourceView(), count));
