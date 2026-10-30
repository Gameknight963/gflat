using Standard::Memory;
namespace Standard::Collections;

namespace detail
{
    private bool AdvancePermutation<T>(Span<T> values,
        int(immutable(T)*, immutable(T)*)* compare, bool forward)
    {
        nuint count = values.Length;
        if (count < 2) return false;
        nuint suffix = count - 1;
        while (suffix != 0)
        {
            int order = compare(&values[suffix - 1], &values[suffix]);
            if ((forward && order < 0) || (!forward && order > 0)) break;
            suffix--;
        }
        if (suffix == 0)
        {
            Standard::Collections::Reverse<T>(values);
            return false;
        }
        nuint pivot = suffix - 1;
        nuint candidate = count - 1;
        while (candidate > pivot)
        {
            int order = compare(&values[pivot], &values[candidate]);
            if ((forward && order < 0) || (!forward && order > 0)) break;
            candidate--;
        }
        // All comparator calls are complete before any initialized owner moves.
        Standard::Collections::Swap<T>(ref values[pivot], ref values[candidate]);
        Standard::Collections::Reverse<T>(values.Slice(suffix));
        return true;
    }
}

public bool NextPermutation<T>(Span<T> values,
    int(immutable(T)*, immutable(T)*)* compare)
    => detail::AdvancePermutation<T>(values, compare, true);

public bool PreviousPermutation<T>(Span<T> values,
    int(immutable(T)*, immutable(T)*)* compare)
    => detail::AdvancePermutation<T>(values, compare, false);
