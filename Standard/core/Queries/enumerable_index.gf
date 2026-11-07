namespace Standard::Queries;

public Enumerable<Indexed<T>, IndexSource<T, S, E>, IndexSource<T, S, E>::Enumerator>
    Index<T, S, E>(Enumerable<T, S, E> source)
    => source.Index();

extension Enumerable<T, S, E>
{
    public Enumerable<Indexed<T>, IndexSource<T, S, E>, IndexSource<T, S, E>::Enumerator> Index()
        => new Enumerable<Indexed<T>, IndexSource<T, S, E>, IndexSource<T, S, E>::Enumerator>(
            new IndexSource<T, S, E>((*this).SourceView()));
}
