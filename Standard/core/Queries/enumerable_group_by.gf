using Standard::Collections;

namespace Standard::Queries;

private static class GroupByProjection<T>
{
    public static T Identity(T value) => move value;
}

public Enumerable<Grouping<K, T>, GroupBySource<T, K, T, S, E>, GroupBySource<T, K, T, S, E>::Enumerator>
    GroupBy<T, K : IHashable, S, E>(Enumerable<T, S, E> source, K(T)* keySelector)
    => source.GroupBy<K>(keySelector);

public Enumerable<Grouping<K, U>, GroupBySource<T, K, U, S, E>, GroupBySource<T, K, U, S, E>::Enumerator>
    GroupBy<T, K : IHashable, U, S, E>(Enumerable<T, S, E> source, K(T)* keySelector, U(T)* elementSelector)
    => source.GroupBy<K, U>(keySelector, elementSelector);

extension Enumerable<T, S, E>
{
    public Enumerable<Grouping<K, T>, GroupBySource<T, K, T, S, E>, GroupBySource<T, K, T, S, E>::Enumerator>
        GroupBy<K : IHashable>(K(T)* keySelector)
        => new Enumerable<Grouping<K, T>, GroupBySource<T, K, T, S, E>, GroupBySource<T, K, T, S, E>::Enumerator>(
            new GroupBySource<T, K, T, S, E>((*this).SourceView(), keySelector, GroupByProjection<T>::Identity));

    public Enumerable<Grouping<K, U>, GroupBySource<T, K, U, S, E>, GroupBySource<T, K, U, S, E>::Enumerator>
        GroupBy<K : IHashable, U>(K(T)* keySelector, U(T)* elementSelector)
        => new Enumerable<Grouping<K, U>, GroupBySource<T, K, U, S, E>, GroupBySource<T, K, U, S, E>::Enumerator>(
            new GroupBySource<T, K, U, S, E>((*this).SourceView(), keySelector, elementSelector));
}
