using Standard::Collections;
namespace Standard::Queries;

// HashMap additionally requires the matching IEquatable<K> contract.
public HashMap<K, T> ToHashMap<T, K : IHashable, S, E>(Enumerable<T, S, E> source, K(T)* keySelector)
=> source.ToHashMap<K>(keySelector);

extension<T, S, E> Enumerable<T, S, E>
{
    public HashMap<K, T> ToHashMap<K : IHashable>(K(T)* keySelector)
    {
        HashMap<K, T> result = new();
        E iterator = (*this).GetEnumerator();
        while (iterator.MoveNext())
        {
            T current = iterator.Current;
            K key = keySelector(current);
            result.Add(move key, move current);
        }
        return move result;
    }
}

public HashMap<K, V> ToHashMap<T, K : IHashable, V, S, E>(Enumerable<T, S, E> source,
    K(T)* keySelector, V(T)* valueSelector)
=> source.ToHashMap<K, V>(keySelector, valueSelector);

extension<T, S, E> Enumerable<T, S, E>
{
    public HashMap<K, V> ToHashMap<K : IHashable, V>(K(T)* keySelector, V(T)* valueSelector)
    {
        HashMap<K, V> result = new();
        E iterator = (*this).GetEnumerator();
        while (iterator.MoveNext())
        {
            T current = iterator.Current;
            K key = keySelector(current);
            V value = valueSelector(current);
            result.Add(move key, move value);
        }
        return move result;
    }
}
