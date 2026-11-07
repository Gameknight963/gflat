namespace Standard::Collections;

public Vector<U> ConvertAll<T, U>(immutable(Vector<T>)* source, U(immutable(T)*)* converter)
{
    Vector<U> result = new();
    result.Reserve(source.Count);
    for (nuint i = 0; i < source.Count; i++)
    {
        U item = converter(&(*source)[i]);
        result.Add(move item);
    }
    return move result;
}

extension Vector<T>
{
    public immutable Vector<U> ConvertAll<U>(U(immutable(T)*)* converter)
        => Standard::Collections::ConvertAll<T, U>(this, converter);
}
