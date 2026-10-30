namespace std::Queries;

public Enumerable<T, DefaultIfEmptySource<T, S, E>, DefaultIfEmptySource<T, S, E>::Enumerator>
    DefaultIfEmpty<T, S, E>(Enumerable<T, S, E> source, T value)
    => source.DefaultIfEmpty(value);

extension<T, S, E> Enumerable<T, S, E>
{
    public Enumerable<T, DefaultIfEmptySource<T, S, E>, DefaultIfEmptySource<T, S, E>::Enumerator> DefaultIfEmpty(T value)
    => new Enumerable<T, DefaultIfEmptySource<T, S, E>, DefaultIfEmptySource<T, S, E>::Enumerator>(
            new DefaultIfEmptySource<T, S, E>((*this).SourceView(), move value));
}
