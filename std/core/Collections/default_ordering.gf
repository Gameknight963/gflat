using std::Memory;
namespace std::Collections;

// Optional comparison-contract conveniences; explicit comparator APIs stay independent.
public void Sort<T : IComparable<T>>(Span<T> values)
    => std::Collections::Sort<T>(values, Comparer<T>::Compare);

public void StableSort<T : IComparable<T>>(Span<T> values)
    => std::Collections::StableSort<T>(values, Comparer<T>::Compare);

public nuint LowerBound<T : IComparable<T>>(Span<immutable(T)> values, immutable(T)* key)
    => std::Collections::LowerBound<T>(values, key, Comparer<T>::Compare);

public nuint UpperBound<T : IComparable<T>>(Span<immutable(T)> values, immutable(T)* key)
    => std::Collections::UpperBound<T>(values, key, Comparer<T>::Compare);

public Span<immutable(T)> EqualRange<T : IComparable<T>>(Span<immutable(T)> values, immutable(T)* key)
    => std::Collections::EqualRange<T>(values, key, Comparer<T>::Compare);

public nuint BinarySearch<T : IComparable<T>>(Span<immutable(T)> values, immutable(T)* key)
    => std::Collections::BinarySearch<T>(values, key, Comparer<T>::Compare);

public bool IsSorted<T : IComparable<T>>(Span<immutable(T)> values)
    => std::Collections::IsSorted<T>(values, Comparer<T>::Compare);

public nuint MinIndex<T : IComparable<T>>(Span<immutable(T)> values)
    => std::Collections::MinIndex<T>(values, Comparer<T>::Compare);

public nuint MaxIndex<T : IComparable<T>>(Span<immutable(T)> values)
    => std::Collections::MaxIndex<T>(values, Comparer<T>::Compare);

public int SequenceCompare<T : IComparable<T>>(Span<immutable(T)> first, Span<immutable(T)> second)
    => std::Collections::SequenceCompare<T>(first, second, Comparer<T>::Compare);

public void NthElement<T : IComparable<T>>(Span<T> values, nuint nth)
    => std::Collections::NthElement<T>(values, nth, Comparer<T>::Compare);

public bool NextPermutation<T : IComparable<T>>(Span<T> values)
    => std::Collections::NextPermutation<T>(values, Comparer<T>::Compare);

public bool PreviousPermutation<T : IComparable<T>>(Span<T> values)
    => std::Collections::PreviousPermutation<T>(values, Comparer<T>::Compare);
