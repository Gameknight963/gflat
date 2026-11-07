using Standard::Collections;

namespace Standard::Queries;

public Enumerable<T, OrderBySource<T, OrderValueKey<T>, S, E>, OrderBySource<T, OrderValueKey<T>, S, E>::Enumerator>
    OrderDescending<T, S, E>(Enumerable<T, S, E> source, int(immutable(T)*, immutable(T)*)* compare)
    => source.OrderDescending(compare);

extension Enumerable<T, S, E>
{
    public Enumerable<T, OrderBySource<T, OrderValueKey<T>, S, E>, OrderBySource<T, OrderValueKey<T>, S, E>::Enumerator>
        OrderDescending(int(immutable(T)*, immutable(T)*)* compare)
        => new Enumerable<T, OrderBySource<T, OrderValueKey<T>, S, E>, OrderBySource<T, OrderValueKey<T>, S, E>::Enumerator>(
            new OrderBySource<T, OrderValueKey<T>, S, E>((*this).SourceView(), new OrderValueKey<T>(compare, true)));
}

public Enumerable<T, OrderBySource<T, OrderValueKey<T>, S, E>, OrderBySource<T, OrderValueKey<T>, S, E>::Enumerator>
    OrderDescending<T : IComparable<T>, S, E>(Enumerable<T, S, E> source)
    => source.OrderDescending();

extension Enumerable<T : IComparable<T>, S, E>
{
    public Enumerable<T, OrderBySource<T, OrderValueKey<T>, S, E>, OrderBySource<T, OrderValueKey<T>, S, E>::Enumerator> OrderDescending()
        => this.OrderDescending(Comparer<T>::Compare);
}
