using Standard::Memory;

namespace Standard::Collections;

public Vector<T> MergeSorted<T : IComparable<T>>(Span<immutable(T)> first, Span<immutable(T)> second)
    => Standard::Collections::MergeSorted<T>(first, second, Comparer<T>::Compare);
