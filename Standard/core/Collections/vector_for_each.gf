namespace Standard::Collections;

// Callbacks borrow elements in index order and must not structurally mutate source.
public void ForEach<T>(immutable(Vector<T>)* source, void(immutable(T)*)* action)
{
    nuint count = source.Count;
    for (nuint i = 0; i < count; i++) action(&(*source)[i]);
}

extension Vector<T>
{
    public immutable void ForEach(void(immutable(T)*)* action)
        => Standard::Collections::ForEach<T>(this, action);
}
