using std::Collections;
namespace std;

// HashMap additionally requires the matching IEquatable<K> contract.
public HashMap<K, T> ToHashMap<T, K : IHashable, S, E>(this Enumerable<T, S, E> source, K(T)* keySelector)
{
    HashMap<K, T> result = new();
    E iterator = source.GetEnumerator();
    while (iterator.MoveNext())
    {
        T current = iterator.Current;
        K key = keySelector(current);
        result.Add(move key, move current);
    }
    return move result;
}

public HashMap<K, V> ToHashMap<T, K : IHashable, V, S, E>(this Enumerable<T, S, E> source,
    K(T)* keySelector, V(T)* valueSelector)
{
    HashMap<K, V> result = new();
    E iterator = source.GetEnumerator();
    while (iterator.MoveNext())
    {
        T current = iterator.Current;
        K key = keySelector(current);
        V value = valueSelector(current);
        result.Add(move key, move value);
    }
    return move result;
}
