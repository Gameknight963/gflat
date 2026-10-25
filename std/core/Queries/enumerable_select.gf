using std::Collections;
namespace std::Queries;

public Enumerable<U, SelectSource<T, U, S, E>, SelectSource<T, U, S, E>::Enumerator> Select<T, U, S, E>(this Enumerable<T, S, E> source, U(T)* selector)
    => new Enumerable<U, SelectSource<T, U, S, E>, SelectSource<T, U, S, E>::Enumerator>(new SelectSource<T, U, S, E>(source.SourceView(), selector));

