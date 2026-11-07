namespace Standard::Collections;

// Copying is instantiated only when this optional operation is used.
public Vector<T> GetRange<T>(immutable(Vector<T>)* source, nuint start, nuint count)
{
    if (start > source.Count || count > source.Count - start)
        throw new Exception("Vector range is out of bounds");
    Vector<T> result = new();
    result.Reserve(count);
    for (nuint i = 0; i < count; i++) result.Add((*source)[start + i]);
    return move result;
}

extension Vector<T>
{
    public immutable Vector<T> GetRange(nuint start, nuint count)
        => Standard::Collections::GetRange<T>(this, start, count);
}
