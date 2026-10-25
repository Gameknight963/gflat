namespace std;

// Optional comparison-contract conveniences; explicit comparator APIs stay independent.
public void Sort<T : IComparable<T>>(Span<T> values)
    => std::Sort<T>(values, Comparer<T>::Compare);

public void StableSort<T : IComparable<T>>(Span<T> values)
    => std::StableSort<T>(values, Comparer<T>::Compare);

public nuint LowerBound<T : IComparable<T>>(Span<immutable(T)> values, immutable(T)* key)
    => std::LowerBound<T>(values, key, Comparer<T>::Compare);

public nuint UpperBound<T : IComparable<T>>(Span<immutable(T)> values, immutable(T)* key)
    => std::UpperBound<T>(values, key, Comparer<T>::Compare);

public nuint BinarySearch<T : IComparable<T>>(Span<immutable(T)> values, immutable(T)* key)
    => std::BinarySearch<T>(values, key, Comparer<T>::Compare);

public bool IsSorted<T : IComparable<T>>(Span<immutable(T)> values)
    => std::IsSorted<T>(values, Comparer<T>::Compare);

public nuint MinIndex<T : IComparable<T>>(Span<immutable(T)> values)
    => std::MinIndex<T>(values, Comparer<T>::Compare);

public nuint MaxIndex<T : IComparable<T>>(Span<immutable(T)> values)
    => std::MaxIndex<T>(values, Comparer<T>::Compare);

public int SequenceCompare<T : IComparable<T>>(Span<immutable(T)> first, Span<immutable(T)> second)
    => std::SequenceCompare<T>(first, second, Comparer<T>::Compare);
