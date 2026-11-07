using Standard::Memory;

namespace Standard::Collections;

public nuint LowerBound<T>(immutable(Vector<T>)* source, immutable(T)* key,
    int(immutable(T)*, immutable(T)*)* compare)
    => Standard::Collections::LowerBound<T>(source, 0, source.Count, key, compare);

public nuint LowerBound<T>(immutable(Vector<T>)* source, nuint start, nuint count, immutable(T)* key,
    int(immutable(T)*, immutable(T)*)* compare)
{
    nuint index = Standard::Collections::LowerBound<T>(source.AsImmutable().Slice(start, count), key, compare);
    return start + index;
}

public nuint LowerBound<T : IComparable<T>>(immutable(Vector<T>)* source, immutable(T)* key)
    => Standard::Collections::LowerBound<T>(source, key, Comparer<T>::Compare);

public nuint LowerBound<T : IComparable<T>>(immutable(Vector<T>)* source, nuint start, nuint count, immutable(T)* key)
    => Standard::Collections::LowerBound<T>(source, start, count, key, Comparer<T>::Compare);

public nuint UpperBound<T>(immutable(Vector<T>)* source, immutable(T)* key,
    int(immutable(T)*, immutable(T)*)* compare)
    => Standard::Collections::UpperBound<T>(source, 0, source.Count, key, compare);

public nuint UpperBound<T>(immutable(Vector<T>)* source, nuint start, nuint count, immutable(T)* key,
    int(immutable(T)*, immutable(T)*)* compare)
{
    nuint index = Standard::Collections::UpperBound<T>(source.AsImmutable().Slice(start, count), key, compare);
    return start + index;
}

public nuint UpperBound<T : IComparable<T>>(immutable(Vector<T>)* source, immutable(T)* key)
    => Standard::Collections::UpperBound<T>(source, key, Comparer<T>::Compare);

public nuint UpperBound<T : IComparable<T>>(immutable(Vector<T>)* source, nuint start, nuint count, immutable(T)* key)
    => Standard::Collections::UpperBound<T>(source, start, count, key, Comparer<T>::Compare);

public nuint BinarySearch<T>(immutable(Vector<T>)* source, immutable(T)* key,
    int(immutable(T)*, immutable(T)*)* compare)
    => Standard::Collections::BinarySearch<T>(source, 0, source.Count, key, compare);

public nuint BinarySearch<T>(immutable(Vector<T>)* source, nuint start, nuint count, immutable(T)* key,
    int(immutable(T)*, immutable(T)*)* compare)
{
    nuint index = Standard::Collections::BinarySearch<T>(source.AsImmutable().Slice(start, count), key, compare);
    if (index == count) return source.Count;
    return start + index;
}

public nuint BinarySearch<T : IComparable<T>>(immutable(Vector<T>)* source, immutable(T)* key)
    => Standard::Collections::BinarySearch<T>(source, key, Comparer<T>::Compare);

public nuint BinarySearch<T : IComparable<T>>(immutable(Vector<T>)* source, nuint start, nuint count, immutable(T)* key)
    => Standard::Collections::BinarySearch<T>(source, start, count, key, Comparer<T>::Compare);

public Span<immutable(T)> EqualRange<T>(immutable(Vector<T>)* source, immutable(T)* key,
    int(immutable(T)*, immutable(T)*)* compare)
    => Standard::Collections::EqualRange<T>(source, 0, source.Count, key, compare);

public Span<immutable(T)> EqualRange<T>(immutable(Vector<T>)* source, nuint start, nuint count, immutable(T)* key,
    int(immutable(T)*, immutable(T)*)* compare)
{
    return Standard::Collections::EqualRange<T>(source.AsImmutable().Slice(start, count), key, compare);
}

public Span<immutable(T)> EqualRange<T : IComparable<T>>(immutable(Vector<T>)* source, immutable(T)* key)
    => Standard::Collections::EqualRange<T>(source, key, Comparer<T>::Compare);

public Span<immutable(T)> EqualRange<T : IComparable<T>>(immutable(Vector<T>)* source, nuint start, nuint count, immutable(T)* key)
    => Standard::Collections::EqualRange<T>(source, start, count, key, Comparer<T>::Compare);

extension Vector<T>
{
    public immutable nuint LowerBound(immutable(T)* key, int(immutable(T)*, immutable(T)*)* compare)
        => Standard::Collections::LowerBound<T>(this, key, compare);
    public immutable nuint LowerBound(nuint start, nuint count, immutable(T)* key,
        int(immutable(T)*, immutable(T)*)* compare)
        => Standard::Collections::LowerBound<T>(this, start, count, key, compare);

    public immutable nuint UpperBound(immutable(T)* key, int(immutable(T)*, immutable(T)*)* compare)
        => Standard::Collections::UpperBound<T>(this, key, compare);
    public immutable nuint UpperBound(nuint start, nuint count, immutable(T)* key,
        int(immutable(T)*, immutable(T)*)* compare)
        => Standard::Collections::UpperBound<T>(this, start, count, key, compare);

    public immutable nuint BinarySearch(immutable(T)* key, int(immutable(T)*, immutable(T)*)* compare)
        => Standard::Collections::BinarySearch<T>(this, key, compare);
    public immutable nuint BinarySearch(nuint start, nuint count, immutable(T)* key,
        int(immutable(T)*, immutable(T)*)* compare)
        => Standard::Collections::BinarySearch<T>(this, start, count, key, compare);

    public immutable Span<immutable(T)> EqualRange(immutable(T)* key, int(immutable(T)*, immutable(T)*)* compare)
        => Standard::Collections::EqualRange<T>(this, key, compare);
    public immutable Span<immutable(T)> EqualRange(nuint start, nuint count, immutable(T)* key,
        int(immutable(T)*, immutable(T)*)* compare)
        => Standard::Collections::EqualRange<T>(this, start, count, key, compare);

}

extension Vector<T : IComparable<T>>
{
    public immutable nuint LowerBound(immutable(T)* key)
        => Standard::Collections::LowerBound<T>(this, key);
    public immutable nuint LowerBound(nuint start, nuint count, immutable(T)* key)
        => Standard::Collections::LowerBound<T>(this, start, count, key);

    public immutable nuint UpperBound(immutable(T)* key)
        => Standard::Collections::UpperBound<T>(this, key);
    public immutable nuint UpperBound(nuint start, nuint count, immutable(T)* key)
        => Standard::Collections::UpperBound<T>(this, start, count, key);

    public immutable nuint BinarySearch(immutable(T)* key)
        => Standard::Collections::BinarySearch<T>(this, key);
    public immutable nuint BinarySearch(nuint start, nuint count, immutable(T)* key)
        => Standard::Collections::BinarySearch<T>(this, start, count, key);

    public immutable Span<immutable(T)> EqualRange(immutable(T)* key)
        => Standard::Collections::EqualRange<T>(this, key);
    public immutable Span<immutable(T)> EqualRange(nuint start, nuint count, immutable(T)* key)
        => Standard::Collections::EqualRange<T>(this, start, count, key);

}
