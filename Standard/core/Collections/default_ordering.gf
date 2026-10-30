using Standard::Memory;
namespace Standard::Collections;

// Optional comparison-contract conveniences; explicit comparator APIs stay independent.
public void Sort<T : IComparable<T>>(Span<T> values)
    => Standard::Collections::Sort<T>(values, Comparer<T>::Compare);

public void StableSort<T : IComparable<T>>(Span<T> values)
    => Standard::Collections::StableSort<T>(values, Comparer<T>::Compare);

public nuint LowerBound<T : IComparable<T>>(Span<immutable(T)> values, immutable(T)* key)
    => Standard::Collections::LowerBound<T>(values, key, Comparer<T>::Compare);

public nuint UpperBound<T : IComparable<T>>(Span<immutable(T)> values, immutable(T)* key)
    => Standard::Collections::UpperBound<T>(values, key, Comparer<T>::Compare);

public Span<immutable(T)> EqualRange<T : IComparable<T>>(Span<immutable(T)> values, immutable(T)* key)
    => Standard::Collections::EqualRange<T>(values, key, Comparer<T>::Compare);

public nuint BinarySearch<T : IComparable<T>>(Span<immutable(T)> values, immutable(T)* key)
    => Standard::Collections::BinarySearch<T>(values, key, Comparer<T>::Compare);

public bool IsSorted<T : IComparable<T>>(Span<immutable(T)> values)
    => Standard::Collections::IsSorted<T>(values, Comparer<T>::Compare);

public nuint MinIndex<T : IComparable<T>>(Span<immutable(T)> values)
    => Standard::Collections::MinIndex<T>(values, Comparer<T>::Compare);

public nuint MaxIndex<T : IComparable<T>>(Span<immutable(T)> values)
    => Standard::Collections::MaxIndex<T>(values, Comparer<T>::Compare);

public int SequenceCompare<T : IComparable<T>>(Span<immutable(T)> first, Span<immutable(T)> second)
    => Standard::Collections::SequenceCompare<T>(first, second, Comparer<T>::Compare);

public void NthElement<T : IComparable<T>>(Span<T> values, nuint nth)
    => Standard::Collections::NthElement<T>(values, nth, Comparer<T>::Compare);

public bool NextPermutation<T : IComparable<T>>(Span<T> values)
    => Standard::Collections::NextPermutation<T>(values, Comparer<T>::Compare);

public bool PreviousPermutation<T : IComparable<T>>(Span<T> values)
    => Standard::Collections::PreviousPermutation<T>(values, Comparer<T>::Compare);

public nuint FindUnsortedIndex<T : IComparable<T>>(Span<immutable(T)> values)
    => Standard::Collections::FindUnsortedIndex<T>(values, Comparer<T>::Compare);

public bool Includes<T : IComparable<T>>(Span<immutable(T)> values, Span<immutable(T)> required)
    => Standard::Collections::Includes<T>(values, required, Comparer<T>::Compare);
