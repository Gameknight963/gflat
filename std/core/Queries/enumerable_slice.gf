using std::Collections;
namespace std::Queries;

public Enumerable<T, SliceSource<T, S, E>, SliceSource<T, S, E>::Enumerator> Take<T, S, E>(Enumerable<T, S, E> source, nuint count)
    => source.Take(count);

extension<T, S, E> Enumerable<T, S, E>
{
    public Enumerable<T, SliceSource<T, S, E>, SliceSource<T, S, E>::Enumerator> Take(nuint count)
    => new Enumerable<T, SliceSource<T, S, E>, SliceSource<T, S, E>::Enumerator>(new SliceSource<T, S, E>((*this).SourceView(), 0, count));
}

public Enumerable<T, SliceSource<T, S, E>, SliceSource<T, S, E>::Enumerator> Skip<T, S, E>(Enumerable<T, S, E> source, nuint count)
    => source.Skip(count);

extension<T, S, E> Enumerable<T, S, E>
{
    public Enumerable<T, SliceSource<T, S, E>, SliceSource<T, S, E>::Enumerator> Skip(nuint count)
    => new Enumerable<T, SliceSource<T, S, E>, SliceSource<T, S, E>::Enumerator>(new SliceSource<T, S, E>((*this).SourceView(), count, (nuint)0 - (nuint)1));
}

