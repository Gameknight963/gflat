using std::Collections;
namespace std::Queries;

public Enumerable<T, ReverseSource<T, S, E>, ReverseSource<T, S, E>::Enumerator>
    Reverse<T, S, E>(this Enumerable<T, S, E> source)
    => new Enumerable<T, ReverseSource<T, S, E>, ReverseSource<T, S, E>::Enumerator>(
        new ReverseSource<T, S, E>(source.SourceView()));
