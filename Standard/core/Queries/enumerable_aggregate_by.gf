using Standard::Collections;

namespace Standard::Queries;

public HashMap<K, U> AggregateBy<T, K : IHashable, U, S, E>(Enumerable<T, S, E> source,
    K(T)* selector, U seed, U(U, T)* accumulate)
    => source.AggregateBy<K, U>(selector, move seed, accumulate);

public HashMap<K, U> AggregateBy<T, K : IHashable, U, S, E>(Enumerable<T, S, E> source,
    K(T)* selector, U(K)* seedSelector, U(U, T)* accumulate)
    => source.AggregateBy<K, U>(selector, seedSelector, accumulate);

extension Enumerable<T, S, E>
{
    public HashMap<K, U> AggregateBy<K : IHashable, U>(K(T)* selector, U seed, U(U, T)* accumulate)
    {
        HashMap<K, U> result = new();
        E iterator = (*this).GetEnumerator();
        while (iterator.MoveNext())
        {
            T current = iterator.Current;
            K key = selector(current);
            U*? existing = result.Find(&key);
            if (existing == null)
            {
                U value = accumulate(seed, move current);
                result.Add(move key, move value);
            }
            else *existing! = accumulate(*existing!, move current);
        }
        return move result;
    }

    public HashMap<K, U> AggregateBy<K : IHashable, U>(K(T)* selector, U(K)* seedSelector, U(U, T)* accumulate)
    {
        HashMap<K, U> result = new();
        E iterator = (*this).GetEnumerator();
        while (iterator.MoveNext())
        {
            T current = iterator.Current;
            K key = selector(current);
            U*? existing = result.Find(&key);
            if (existing == null)
            {
                U seed = seedSelector(key);
                U value = accumulate(move seed, move current);
                result.Add(move key, move value);
            }
            else *existing! = accumulate(*existing!, move current);
        }
        return move result;
    }
}
