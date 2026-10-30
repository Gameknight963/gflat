namespace std::Queries;

public Enumerable<T, EndpointSource<T, S, E>, EndpointSource<T, S, E>::Enumerator>
    Append<T, S, E>(Enumerable<T, S, E> source, T value)
    => source.Append(value);

extension<T, S, E> Enumerable<T, S, E>
{
    public Enumerable<T, EndpointSource<T, S, E>, EndpointSource<T, S, E>::Enumerator> Append(T value)
    => new Enumerable<T, EndpointSource<T, S, E>, EndpointSource<T, S, E>::Enumerator>(
            new EndpointSource<T, S, E>((*this).SourceView(), move value, false));
}

public Enumerable<T, EndpointSource<T, S, E>, EndpointSource<T, S, E>::Enumerator>
    Prepend<T, S, E>(Enumerable<T, S, E> source, T value)
    => source.Prepend(value);

extension<T, S, E> Enumerable<T, S, E>
{
    public Enumerable<T, EndpointSource<T, S, E>, EndpointSource<T, S, E>::Enumerator> Prepend(T value)
    => new Enumerable<T, EndpointSource<T, S, E>, EndpointSource<T, S, E>::Enumerator>(
            new EndpointSource<T, S, E>((*this).SourceView(), move value, true));
}
