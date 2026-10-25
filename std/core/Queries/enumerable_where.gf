using std::Collections;
namespace std::Queries;

public Enumerable<T, WhereSource<T, S, E>, WhereSource<T, S, E>::Enumerator> Where<T, S, E>(this Enumerable<T, S, E> source, bool(T)* predicate)
    => new Enumerable<T, WhereSource<T, S, E>, WhereSource<T, S, E>::Enumerator>(new WhereSource<T, S, E>(source.SourceView(), predicate));

