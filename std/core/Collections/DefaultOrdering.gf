namespace std;

// Optional comparison-contract conveniences; explicit comparator APIs stay independent.
public void Sort<T : IComparable<T>>(Span<T> values)
    => std::Sort<T>(values, Comparer<T>::Compare);

public void StableSort<T : IComparable<T>>(Span<T> values)
    => std::StableSort<T>(values, Comparer<T>::Compare);
