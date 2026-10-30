using std::Collections;
namespace std::Queries;

public nuint Count<T, S, E>(Enumerable<T, S, E> source)
=> source.Count();

extension<T, S, E> Enumerable<T, S, E>
{
    public nuint Count()
    {
        nuint count = 0;
        E iterator = (*this).GetEnumerator();
        while (iterator.MoveNext())
        {
            if (count == (nuint)0 - (nuint)1) throw new Exception("Sequence count overflow");
            count++;
        }
        return count;
    }
}

public nuint Count<T, S, E>(Enumerable<T, S, E> source, bool(T)* predicate)
=> source.Count(predicate);

extension<T, S, E> Enumerable<T, S, E>
{
    public nuint Count(bool(T)* predicate)
    {
        nuint count = 0;
        E iterator = (*this).GetEnumerator();
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
}

