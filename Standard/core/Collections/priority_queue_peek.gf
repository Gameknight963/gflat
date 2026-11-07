namespace Standard::Collections;

public T Peek<T>(immutable(PriorityQueue<T>)* source)
{
    return source.Top;
}

public bool TryPeek<T>(immutable(PriorityQueue<T>)* source, ref T output)
{
    if (source.IsEmpty) return false;
    nuint start = (&source.Top).reinterpret<nuint>();
    nuint address = (&output).reinterpret<nuint>();
    nuint stride = (nuint)sizeof(T);
    if (stride == 0) stride = 1;
    if (address >= start && address - start < source.Capacity * stride)
        throw new Exception("Priority queue output must not refer to its storage");
    T copy = source.Top;
    output = move copy;
    return true;
}

extension PriorityQueue<T>
{
    public immutable T Peek()
        => Standard::Collections::Peek<T>(this);

    public immutable bool TryPeek(ref T output)
        => Standard::Collections::TryPeek<T>(this, ref output);
}
