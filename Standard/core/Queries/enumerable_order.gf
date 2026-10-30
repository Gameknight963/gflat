using Standard::Collections;
namespace Standard::Queries;

public Enumerable<T, OrderSource<T, S, E>, OrderSource<T, S, E>::Enumerator>
    Order<T, S, E>(Enumerable<T, S, E> source, int(immutable(T)*, immutable(T)*)* compare)
    => source.Order(compare);

extension<T, S, E> Enumerable<T, S, E>
{
    public Enumerable<T, OrderSource<T, S, E>, OrderSource<T, S, E>::Enumerator> Order(int(immutable(T)*, immutable(T)*)* compare)
    => new Enumerable<T, OrderSource<T, S, E>, OrderSource<T, S, E>::Enumerator>(
            new OrderSource<T, S, E>((*this).SourceView(), compare));
}

public Enumerable<T, OrderSource<T, S, E>, OrderSource<T, S, E>::Enumerator>
    Order<T : IComparable<T>, S, E>(Enumerable<T, S, E> source)
    => source.Order();

extension<T : IComparable<T>, S, E> Enumerable<T, S, E>
{
    public Enumerable<T, OrderSource<T, S, E>, OrderSource<T, S, E>::Enumerator> Order()
    => this.Order( Comparer<T>::Compare);
}
