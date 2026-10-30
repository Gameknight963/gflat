using Standard::Collections;
namespace Standard::Queries;

public U Aggregate<T, U, S, E>(Enumerable<T, S, E> source, U seed, U(U, T)* accumulate)
=> source.Aggregate<U>(seed, accumulate);

extension<T, S, E> Enumerable<T, S, E>
{
    public U Aggregate<U>(U seed, U(U, T)* accumulate)
    {
        E iterator = (*this).GetEnumerator();
        while (iterator.MoveNext()) seed = accumulate(move seed, iterator.Current);
        return move seed;
    }
}

public T Aggregate<T, S, E>(Enumerable<T, S, E> source, T(T, T)* accumulate)
=> source.Aggregate(accumulate);

extension<T, S, E> Enumerable<T, S, E>
{
    public T Aggregate(T(T, T)* accumulate)
    {
        E iterator = (*this).GetEnumerator();
        if (!iterator.MoveNext()) throw new Exception("Sequence is empty");
        T result = iterator.Current;
        while (iterator.MoveNext()) result = accumulate(move result, iterator.Current);
        return move result;
    }
}

