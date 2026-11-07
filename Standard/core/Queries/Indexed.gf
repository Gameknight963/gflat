namespace Standard::Queries;

public struct Indexed<T>
{
    public nuint Index;
    public T Item;

    public Indexed(nuint index, T item)
    {
        Index = index;
        Item = move item;
    }
}
