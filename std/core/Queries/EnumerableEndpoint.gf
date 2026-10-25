namespace std;

public Enumerable<T, EndpointSource<T, S, E>, EndpointSource<T, S, E>::Enumerator>
    Append<T, S, E>(this Enumerable<T, S, E> source, T value)
    => new Enumerable<T, EndpointSource<T, S, E>, EndpointSource<T, S, E>::Enumerator>(
        new EndpointSource<T, S, E>(source.SourceView(), move value, false));

public Enumerable<T, EndpointSource<T, S, E>, EndpointSource<T, S, E>::Enumerator>
    Prepend<T, S, E>(this Enumerable<T, S, E> source, T value)
    => new Enumerable<T, EndpointSource<T, S, E>, EndpointSource<T, S, E>::Enumerator>(
        new EndpointSource<T, S, E>(source.SourceView(), move value, true));
