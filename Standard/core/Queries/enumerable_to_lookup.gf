using Standard::Collections;
namespace Standard::Queries;

// Eager owning groups; HashMap additionally needs the matching IEquatable<K>.
public HashMap<K, Vector<T>> ToLookup<T, K : IHashable, S, E>(Enumerable<T, S, E> source, K(T)* keySelector)
=> source.ToLookup<K>(keySelector);

extension<T, S, E> Enumerable<T, S, E>
{
    public HashMap<K, Vector<T>> ToLookup<K : IHashable>(K(T)* keySelector)
    {
        HashMap<K, Vector<T>> result = new();
        E iterator = (*this).GetEnumerator();
        while (iterator.MoveNext())
        {
            T current = iterator.Current;
            K key = keySelector(current);
            Vector<T>*? existing = result.Find(&key);
            if (existing != null) existing!.Add(move current);
            else
            {
                Vector<T> group = new();
                group.Add(move current);
                result.Add(move key, move group);
            }
        }
        return move result;
    }
}

public HashMap<K, Vector<V>> ToLookup<T, K : IHashable, V, S, E>(Enumerable<T, S, E> source,
    K(T)* keySelector, V(T)* valueSelector)
=> source.ToLookup<K, V>(keySelector, valueSelector);

extension<T, S, E> Enumerable<T, S, E>
{
    public HashMap<K, Vector<V>> ToLookup<K : IHashable, V>(K(T)* keySelector, V(T)* valueSelector)
    {
        HashMap<K, Vector<V>> result = new();
        E iterator = (*this).GetEnumerator();
        while (iterator.MoveNext())
        {
            T current = iterator.Current;
            K key = keySelector(current);
            V value = valueSelector(current);
            Vector<V>*? existing = result.Find(&key);
            if (existing != null) existing!.Add(move value);
            else
            {
                Vector<V> group = new();
                group.Add(move value);
                result.Add(move key, move group);
            }
        }
        return move result;
    }
}
