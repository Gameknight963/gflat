using Standard::Collections;

namespace Standard::Queries;

public Enumerable<T, OrderBySource<T, ThenOrderKey<T, OrderValueKey<T>, K>, S, E>, OrderBySource<T, ThenOrderKey<T, OrderValueKey<T>, K>, S, E>::Enumerator>
    ThenBy<T, K, S, E, Iterator>(Enumerable<T, OrderSource<T, S, E>, Iterator> source,
        K(T)* selector, int(immutable(K)*, immutable(K)*)* compare)
    => source.ThenBy<K>(selector, compare);

extension Enumerable<T, OrderSource<T, S, E>, Iterator>
{
    public Enumerable<T, OrderBySource<T, ThenOrderKey<T, OrderValueKey<T>, K>, S, E>, OrderBySource<T, ThenOrderKey<T, OrderValueKey<T>, K>, S, E>::Enumerator>
        ThenBy<K>(K(T)* selector, int(immutable(K)*, immutable(K)*)* compare)
    {
        OrderSource<T, S, E> prior = (*this).SourceView();
        return new Enumerable<T, OrderBySource<T, ThenOrderKey<T, OrderValueKey<T>, K>, S, E>, OrderBySource<T, ThenOrderKey<T, OrderValueKey<T>, K>, S, E>::Enumerator>(
            new OrderBySource<T, ThenOrderKey<T, OrderValueKey<T>, K>, S, E>(prior.InputView(),
                new ThenOrderKey<T, OrderValueKey<T>, K>(new OrderValueKey<T>(prior.Comparison(), false), selector, compare, false)));
    }
}

public Enumerable<T, OrderBySource<T, ThenOrderKey<T, OrderValueKey<T>, K>, S, E>, OrderBySource<T, ThenOrderKey<T, OrderValueKey<T>, K>, S, E>::Enumerator>
    ThenBy<T, K : IComparable<K>, S, E, Iterator>(Enumerable<T, OrderSource<T, S, E>, Iterator> source,
        K(T)* selector)
    => source.ThenBy<K>(selector);

extension Enumerable<T, OrderSource<T, S, E>, Iterator>
{
    public Enumerable<T, OrderBySource<T, ThenOrderKey<T, OrderValueKey<T>, K>, S, E>, OrderBySource<T, ThenOrderKey<T, OrderValueKey<T>, K>, S, E>::Enumerator> ThenBy<K : IComparable<K>>(K(T)* selector)
        => this.ThenBy<K>(selector, Comparer<K>::Compare);
}

public Enumerable<T, OrderBySource<T, ThenOrderKey<T, OrderValueKey<T>, K>, S, E>, OrderBySource<T, ThenOrderKey<T, OrderValueKey<T>, K>, S, E>::Enumerator>
    ThenByDescending<T, K, S, E, Iterator>(Enumerable<T, OrderSource<T, S, E>, Iterator> source,
        K(T)* selector, int(immutable(K)*, immutable(K)*)* compare)
    => source.ThenByDescending<K>(selector, compare);

extension Enumerable<T, OrderSource<T, S, E>, Iterator>
{
    public Enumerable<T, OrderBySource<T, ThenOrderKey<T, OrderValueKey<T>, K>, S, E>, OrderBySource<T, ThenOrderKey<T, OrderValueKey<T>, K>, S, E>::Enumerator>
        ThenByDescending<K>(K(T)* selector, int(immutable(K)*, immutable(K)*)* compare)
    {
        OrderSource<T, S, E> prior = (*this).SourceView();
        return new Enumerable<T, OrderBySource<T, ThenOrderKey<T, OrderValueKey<T>, K>, S, E>, OrderBySource<T, ThenOrderKey<T, OrderValueKey<T>, K>, S, E>::Enumerator>(
            new OrderBySource<T, ThenOrderKey<T, OrderValueKey<T>, K>, S, E>(prior.InputView(),
                new ThenOrderKey<T, OrderValueKey<T>, K>(new OrderValueKey<T>(prior.Comparison(), false), selector, compare, true)));
    }
}

public Enumerable<T, OrderBySource<T, ThenOrderKey<T, OrderValueKey<T>, K>, S, E>, OrderBySource<T, ThenOrderKey<T, OrderValueKey<T>, K>, S, E>::Enumerator>
    ThenByDescending<T, K : IComparable<K>, S, E, Iterator>(Enumerable<T, OrderSource<T, S, E>, Iterator> source,
        K(T)* selector)
    => source.ThenByDescending<K>(selector);

extension Enumerable<T, OrderSource<T, S, E>, Iterator>
{
    public Enumerable<T, OrderBySource<T, ThenOrderKey<T, OrderValueKey<T>, K>, S, E>, OrderBySource<T, ThenOrderKey<T, OrderValueKey<T>, K>, S, E>::Enumerator> ThenByDescending<K : IComparable<K>>(K(T)* selector)
        => this.ThenByDescending<K>(selector, Comparer<K>::Compare);
}
