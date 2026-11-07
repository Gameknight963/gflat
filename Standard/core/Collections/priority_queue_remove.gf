namespace Standard::Collections;

public bool Remove<T>(ref PriorityQueue<T> source, immutable(T)* value, ref T output,
    bool(immutable(T)*, immutable(T)*)* equal)
    => source.RemoveBy(value, ref output, equal);

public bool Remove<T : IEquatable<T>>(ref PriorityQueue<T> source, immutable(T)* value, ref T output)
    => source.RemoveBy(value, ref output, EqualityComparer<T>::Equals);

extension PriorityQueue<T>
{
    public bool Remove(immutable(T)* value, ref T output,
        bool(immutable(T)*, immutable(T)*)* equal)
        => (*this).RemoveBy(value, ref output, equal);
}

extension PriorityQueue<T : IEquatable<T>>
{
    public bool Remove(immutable(T)* value, ref T output)
        => (*this).RemoveBy(value, ref output, EqualityComparer<T>::Equals);
}
