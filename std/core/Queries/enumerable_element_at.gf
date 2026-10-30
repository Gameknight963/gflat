namespace std::Queries;

public T ElementAt<T, S, E>(Enumerable<T, S, E> source, nuint index)
=> source.ElementAt(index);

extension<T, S, E> Enumerable<T, S, E>
{
    public T ElementAt(nuint index)
    {
        E iterator = (*this).GetEnumerator();
        while (iterator.MoveNext())
        {
            if (index == 0) return iterator.Current;
            index--;
        }
        throw new Exception("Sequence index out of range", 3);
    }
}

// Leaves the initialized result unchanged when the sequence ends first.
public bool TryElementAt<T, S, E>(Enumerable<T, S, E> source, nuint index, ref T result)
=> source.TryElementAt(index, ref result);

extension<T, S, E> Enumerable<T, S, E>
{
    public bool TryElementAt(nuint index, ref T result)
    {
        E iterator = (*this).GetEnumerator();
        while (iterator.MoveNext())
        {
            if (index == 0) { result = iterator.Current; return true; }
            index--;
        }
        return false;
    }
}
