namespace Standard::Collections;

public void Reverse<T>(Vector<T>* source)
    => Standard::Collections::Reverse<T>(source.AsSpan());

public void Reverse<T>(Vector<T>* source, nuint start, nuint count)
    => Standard::Collections::Reverse<T>(source.AsSpan().Slice(start, count));

public void Rotate<T>(Vector<T>* source, nuint middle)
    => Standard::Collections::Rotate<T>(source.AsSpan(), middle);

public void Rotate<T>(Vector<T>* source, nuint start, nuint count, nuint middle)
    => Standard::Collections::Rotate<T>(source.AsSpan().Slice(start, count), middle);

public void Sort<T>(Vector<T>* source, int(immutable(T)*, immutable(T)*)* compare)
    => Standard::Collections::Sort<T>(source.AsSpan(), compare);

public void Sort<T>(Vector<T>* source, nuint start, nuint count,
    int(immutable(T)*, immutable(T)*)* compare)
    => Standard::Collections::Sort<T>(source.AsSpan().Slice(start, count), compare);

public void Sort<T : IComparable<T>>(Vector<T>* source)
    => Standard::Collections::Sort<T>(source.AsSpan(), Comparer<T>::Compare);

public void Sort<T : IComparable<T>>(Vector<T>* source, nuint start, nuint count)
    => Standard::Collections::Sort<T>(source.AsSpan().Slice(start, count), Comparer<T>::Compare);

extension Vector<T>
{
    public void Reverse() => Standard::Collections::Reverse<T>(this);
    public void Reverse(nuint start, nuint count) => Standard::Collections::Reverse<T>(this, start, count);
    public void Rotate(nuint middle) => Standard::Collections::Rotate<T>(this, middle);
    public void Rotate(nuint start, nuint count, nuint middle)
        => Standard::Collections::Rotate<T>(this, start, count, middle);
    public void Sort(int(immutable(T)*, immutable(T)*)* compare)
        => Standard::Collections::Sort<T>(this, compare);
    public void Sort(nuint start, nuint count, int(immutable(T)*, immutable(T)*)* compare)
        => Standard::Collections::Sort<T>(this, start, count, compare);
}

extension Vector<T : IComparable<T>>
{
    public void Sort() => Standard::Collections::Sort<T>(this);
    public void Sort(nuint start, nuint count) => Standard::Collections::Sort<T>(this, start, count);
}
