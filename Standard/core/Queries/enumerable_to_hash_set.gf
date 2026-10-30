using Standard::Collections;
namespace Standard::Queries;

// Materializes ordinary value copies; duplicate arguments are destroyed by Add.
public HashSet<T> ToHashSet<T : IHashable, S, E>(Enumerable<T, S, E> source)
=> source.ToHashSet();

extension<T : IHashable, S, E> Enumerable<T, S, E>
{
    public HashSet<T> ToHashSet()
    {
        HashSet<T> result = new HashSet<T>();
        E iterator = (*this).GetEnumerator();
        while (iterator.MoveNext()) result.Add(iterator.Current);
        return move result;
    }
}
