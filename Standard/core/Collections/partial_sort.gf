using Standard::Memory;

namespace Standard::Collections;

// Sort the smallest count elements into the prefix. The remaining order is
// unspecified. Comparator failure can leave a partial ordering; values stay owned.
public void PartialSort<T>(Span<T> values, nuint count,
    int(immutable(T)*, immutable(T)*)* compare)
{
    if (count > values.Length) throw new Exception("Partial sort count out of range", 3);
    if (count == 0) return;
    Span<T> heap = values.Slice(0, count);
    Standard::Collections::MakeMaxHeap<T>(heap, compare);
    for (nuint index = count; index < values.Length; index++)
    {
        if (compare(&values[index], &values[0]) < 0)
        {
            Standard::Collections::PopMaxHeap<T>(heap, compare);
            Standard::Collections::Swap<T>(ref values[count - 1], ref values[index]);
            Standard::Collections::PushMaxHeap<T>(heap, compare);
        }
    }
    while (count > 1)
    {
        Standard::Collections::PopMaxHeap<T>(values.Slice(0, count), compare);
        count--;
    }
}
