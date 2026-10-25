using std::Collections;
namespace std::Queries;

public U Aggregate<T, U, S, E>(this Enumerable<T, S, E> source, U seed, U(U, T)* accumulate)
{
    E iterator = source.GetEnumerator();
    while (iterator.MoveNext()) seed = accumulate(move seed, iterator.Current);
    return move seed;
}

public T Aggregate<T, S, E>(this Enumerable<T, S, E> source, T(T, T)* accumulate)
{
    E iterator = source.GetEnumerator();
    if (!iterator.MoveNext()) throw new Exception("Sequence is empty");
    T result = iterator.Current;
    while (iterator.MoveNext()) result = accumulate(move result, iterator.Current);
    return move result;
}

