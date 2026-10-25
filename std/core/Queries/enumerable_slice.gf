using std::Collections;
namespace std::Queries;

public Enumerable<T, SliceSource<T, S, E>, SliceSource<T, S, E>::Enumerator> Take<T, S, E>(this Enumerable<T, S, E> source, nuint count)
    => new Enumerable<T, SliceSource<T, S, E>, SliceSource<T, S, E>::Enumerator>(new SliceSource<T, S, E>(source.SourceView(), 0, count));

public Enumerable<T, SliceSource<T, S, E>, SliceSource<T, S, E>::Enumerator> Skip<T, S, E>(this Enumerable<T, S, E> source, nuint count)
    => new Enumerable<T, SliceSource<T, S, E>, SliceSource<T, S, E>::Enumerator>(new SliceSource<T, S, E>(source.SourceView(), count, (nuint)0 - (nuint)1));

