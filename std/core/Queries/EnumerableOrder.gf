using std::Collections;
namespace std::Queries;

public Enumerable<T, OrderSource<T, S, E>, OrderSource<T, S, E>::Enumerator>
    Order<T, S, E>(this Enumerable<T, S, E> source, int(immutable(T)*, immutable(T)*)* compare)
    => new Enumerable<T, OrderSource<T, S, E>, OrderSource<T, S, E>::Enumerator>(
        new OrderSource<T, S, E>(source.SourceView(), compare));

public Enumerable<T, OrderSource<T, S, E>, OrderSource<T, S, E>::Enumerator>
    Order<T : IComparable<T>, S, E>(this Enumerable<T, S, E> source)
    => std::Queries::Order<T, S, E>(move source, Comparer<T>::Compare);
