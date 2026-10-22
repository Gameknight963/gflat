namespace std;

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
