namespace std::Queries;

public T ElementAt<T, S, E>(this Enumerable<T, S, E> source, nuint index)
{
    E iterator = source.GetEnumerator();
    while (iterator.MoveNext())
    {
        if (index == 0) return iterator.Current;
        index--;
    }
    throw new Exception("Sequence index out of range", 3);
}

// Leaves the initialized result unchanged when the sequence ends first.
public bool TryElementAt<T, S, E>(this Enumerable<T, S, E> source, nuint index, ref T result)
{
    E iterator = source.GetEnumerator();
    while (iterator.MoveNext())
    {
        if (index == 0) { result = iterator.Current; return true; }
        index--;
    }
    return false;
}
