namespace Standard::Collections;

// A value-producing iterator. Borrowing iterators can expose ref Current and
// use the foreach pattern directly instead.
interface IEnumerator<T>
{
    bool MoveNext();
    T Current { get; }
}
