namespace Standard::Collections;

public T Peek<T>(immutable(Stack<T>)* source)
{
    return source.Top;
}

public bool TryPeek<T>(immutable(Stack<T>)* source, ref T output)
{
    if (source.IsEmpty) return false;
    T copy = source.Top;
    output = move copy;
    return true;
}

extension Stack<T>
{
    public immutable T Peek()
        => Standard::Collections::Peek<T>(this);

    public immutable bool TryPeek(ref T output)
        => Standard::Collections::TryPeek<T>(this, ref output);
}
