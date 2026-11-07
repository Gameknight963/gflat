namespace Standard::Collections;

public T Peek<T>(immutable(Queue<T>)* source)
{
    return source.Front;
}

public bool TryPeek<T>(immutable(Queue<T>)* source, ref T output)
{
    if (source.IsEmpty) return false;
    T copy = source.Front;
    output = move copy;
    return true;
}

extension Queue<T>
{
    public immutable T Peek()
        => Standard::Collections::Peek<T>(this);

    public immutable bool TryPeek(ref T output)
        => Standard::Collections::TryPeek<T>(this, ref output);
}
