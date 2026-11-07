namespace Standard::Collections;

public T PeekFirst<T>(immutable(Deque<T>)* source)
{
    return source.Front;
}

public bool TryPeekFirst<T>(immutable(Deque<T>)* source, ref T output)
{
    if (source.IsEmpty) return false;
    T copy = source.Front;
    output = move copy;
    return true;
}

public T PeekLast<T>(immutable(Deque<T>)* source)
{
    return source.Back;
}

public bool TryPeekLast<T>(immutable(Deque<T>)* source, ref T output)
{
    if (source.IsEmpty) return false;
    T copy = source.Back;
    output = move copy;
    return true;
}

extension Deque<T>
{
    public immutable T PeekFirst()
        => Standard::Collections::PeekFirst<T>(this);

    public immutable bool TryPeekFirst(ref T output)
        => Standard::Collections::TryPeekFirst<T>(this, ref output);

    public immutable T PeekLast()
        => Standard::Collections::PeekLast<T>(this);

    public immutable bool TryPeekLast(ref T output)
        => Standard::Collections::TryPeekLast<T>(this, ref output);
}
