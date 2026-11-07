namespace Standard::Collections;

public bool TryPeek<T>(immutable(Stack<T>)* source, ref T output)
{
    if (source.IsEmpty) return false;
    T copy = source.Top;
    output = move copy;
    return true;
}

extension Stack<T>
{
    public immutable bool TryPeek(ref T output)
        => Standard::Collections::TryPeek<T>(this, ref output);
}
