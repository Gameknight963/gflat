namespace Standard::Collections;

public bool TryPeek<T>(immutable(Queue<T>)* source, ref T output)
{
    if (source.IsEmpty) return false;
    T copy = source.Front;
    output = move copy;
    return true;
}

extension Queue<T>
{
    public immutable bool TryPeek(ref T output)
        => Standard::Collections::TryPeek<T>(this, ref output);
}
