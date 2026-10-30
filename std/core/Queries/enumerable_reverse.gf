using std::Collections;
namespace std::Queries;

public Enumerable<T, ReverseSource<T, S, E>, ReverseSource<T, S, E>::Enumerator>
    Reverse<T, S, E>(Enumerable<T, S, E> source)
    => source.Reverse();

extension<T, S, E> Enumerable<T, S, E>
{
    public Enumerable<T, ReverseSource<T, S, E>, ReverseSource<T, S, E>::Enumerator> Reverse()
    => new Enumerable<T, ReverseSource<T, S, E>, ReverseSource<T, S, E>::Enumerator>(
            new ReverseSource<T, S, E>((*this).SourceView()));
}
