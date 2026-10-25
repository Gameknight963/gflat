using std::Collections;
namespace std::Queries;

public nuint Count<T, S, E>(this Enumerable<T, S, E> source)
{
    nuint count = 0;
    E iterator = source.GetEnumerator();
    while (iterator.MoveNext())
    {
        if (count == (nuint)0 - (nuint)1) throw new Exception("Sequence count overflow");
        count++;
    }
    return count;
}

public nuint Count<T, S, E>(this Enumerable<T, S, E> source, bool(T)* predicate)
{
    nuint count = 0;
    E iterator = source.GetEnumerator();
    while (iterator.MoveNext())
    {
        if (predicate(iterator.Current))
        {
            if (count == (nuint)0 - (nuint)1) throw new Exception("Sequence count overflow");
            count++;
        }
    }
    return count;
}

