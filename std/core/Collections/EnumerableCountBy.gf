namespace std;

// Materializes owning keys. HashMap also requires the matching IEquatable<K>.
public HashMap<K, nuint> CountBy<T, K : IHashable, S, E>(this Enumerable<T, S, E> source, K(T)* selector)
{
    HashMap<K, nuint> result = new();
    E iterator = source.GetEnumerator();
    while (iterator.MoveNext())
    {
        T current = iterator.Current;
        K key = selector(current);
        nuint*? existing = result.Find(&key);
        if (existing == null) result.Add(move key, 1);
        else
        {
            if (*existing! == (nuint)0 - (nuint)1) throw new Exception("Sequence count overflow");
            (*existing!)++;
        }
    }
    return move result;
}
