namespace std;

public Enumerable<T, TakeLastSource<T, S, E>, TakeLastSource<T, S, E>::Enumerator>
    TakeLast<T, S, E>(this Enumerable<T, S, E> source, nuint count)
    => new Enumerable<T, TakeLastSource<T, S, E>, TakeLastSource<T, S, E>::Enumerator>(
        new TakeLastSource<T, S, E>(source.SourceView(), count));
