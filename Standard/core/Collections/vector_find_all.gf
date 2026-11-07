namespace Standard::Collections;

public Vector<T> FindAll<T>(immutable(Vector<T>)* source, bool(immutable(T)*)* predicate)
{
    Vector<T> result = new();
    for (nuint i = 0; i < source.Count; i++)
        if (predicate(&(*source)[i])) result.Add((*source)[i]);
    return move result;
}

extension Vector<T>
{
    public immutable Vector<T> FindAll(bool(immutable(T)*)* predicate)
        => Standard::Collections::FindAll<T>(this, predicate);
}
