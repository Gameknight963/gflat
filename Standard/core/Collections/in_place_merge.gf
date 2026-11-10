using Standard::Memory;

namespace Standard::Collections;

namespace detail
{
    private void MergeAdjacentRanges<T>(Span<T> values, nuint middle,
        int(immutable(T)*, immutable(T)*)* compare)
    {
        nuint count = values.Length;
        if (middle == 0 || middle == count) return;
        if (compare(&values[middle - 1], &values[middle]) <= 0) return;
        if (count == 2)
        {
            Standard::Collections::Swap<T>(ref values[0], ref values[1]);
            return;
        }

        nuint firstCut;
        nuint secondCut;
        if (middle > count - middle)
        {
            firstCut = middle / 2;
            secondCut = middle + Standard::Collections::LowerBound<T>(
                values.Slice(middle).AsImmutable(), &values[firstCut], compare);
        }
        else
        {
            secondCut = middle + (count - middle) / 2;
            firstCut = Standard::Collections::UpperBound<T>(
                values.Slice(0, middle).AsImmutable(), &values[secondCut], compare);
        }

        nuint joined = firstCut + (secondCut - middle);
        Standard::Collections::Rotate<T>(values.Slice(firstCut, secondCut - firstCut), middle - firstCut);
        Standard::Collections::detail::MergeAdjacentRanges<T>(values.Slice(0, joined), firstCut, compare);
        Standard::Collections::detail::MergeAdjacentRanges<T>(values.Slice(joined), secondCut - joined, compare);
    }
}

// Stably merge sorted [0, middle) and [middle, Length) without allocation.
// Uses O(n log n) relocations and O(log n) stack storage. Comparator failure can
// leave a partial rearrangement. Every element remains owned by the span.
public void InPlaceMerge<T>(Span<T> values, nuint middle,
    int(immutable(T)*, immutable(T)*)* compare)
{
    if (middle > values.Length) throw new Exception("Merge boundary out of range", 3);
    Standard::Collections::detail::MergeAdjacentRanges<T>(values, middle, compare);
}
