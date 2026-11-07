using Standard::Collections;

namespace Standard::Queries;

public Enumerable<T, OrderBySource<T, K, S, E>, OrderBySource<T, K, S, E>::Enumerator>
    OrderBy<T, K, S, E>(Enumerable<T, S, E> source, K(T)* selector,
        int(immutable(K)*, immutable(K)*)* compare)
    => source.OrderBy<K>(selector, compare);

extension Enumerable<T, S, E>
{
    public Enumerable<T, OrderBySource<T, K, S, E>, OrderBySource<T, K, S, E>::Enumerator>
        OrderBy<K>(K(T)* selector, int(immutable(K)*, immutable(K)*)* compare)
        => new Enumerable<T, OrderBySource<T, K, S, E>, OrderBySource<T, K, S, E>::Enumerator>(
            new OrderBySource<T, K, S, E>((*this).SourceView(), selector, compare, false));
}

public Enumerable<T, OrderBySource<T, K, S, E>, OrderBySource<T, K, S, E>::Enumerator>
    OrderBy<T, K : IComparable<K>, S, E>(Enumerable<T, S, E> source, K(T)* selector)
    => source.OrderBy<K>(selector);

extension Enumerable<T, S, E>
{
    public Enumerable<T, OrderBySource<T, K, S, E>, OrderBySource<T, K, S, E>::Enumerator> OrderBy<K : IComparable<K>>(K(T)* selector)
        => this.OrderBy<K>(selector, Comparer<K>::Compare);
}

public Enumerable<T, OrderBySource<T, K, S, E>, OrderBySource<T, K, S, E>::Enumerator>
    OrderByDescending<T, K, S, E>(Enumerable<T, S, E> source, K(T)* selector,
        int(immutable(K)*, immutable(K)*)* compare)
    => source.OrderByDescending<K>(selector, compare);

extension Enumerable<T, S, E>
{
    public Enumerable<T, OrderBySource<T, K, S, E>, OrderBySource<T, K, S, E>::Enumerator>
        OrderByDescending<K>(K(T)* selector, int(immutable(K)*, immutable(K)*)* compare)
        => new Enumerable<T, OrderBySource<T, K, S, E>, OrderBySource<T, K, S, E>::Enumerator>(
            new OrderBySource<T, K, S, E>((*this).SourceView(), selector, compare, true));
}

public Enumerable<T, OrderBySource<T, K, S, E>, OrderBySource<T, K, S, E>::Enumerator>
    OrderByDescending<T, K : IComparable<K>, S, E>(Enumerable<T, S, E> source, K(T)* selector)
    => source.OrderByDescending<K>(selector);

extension Enumerable<T, S, E>
{
    public Enumerable<T, OrderBySource<T, K, S, E>, OrderBySource<T, K, S, E>::Enumerator> OrderByDescending<K : IComparable<K>>(K(T)* selector)
        => this.OrderByDescending<K>(selector, Comparer<K>::Compare);
}
