using Standard::Memory;

namespace Standard::Collections;

namespace detail
{
    private nuint StablePartitionHalves<T>(Span<T> values, bool(immutable(T)*)* predicate)
    {
        nuint count = values.Length;
        if (count == 0) return 0;
        if (count == 1)
        {
            if (predicate(&values[0])) return 1;
            return 0;
        }
        nuint middle = count / 2;
        nuint left = Standard::Collections::detail::StablePartitionHalves<T>(values.Slice(0, middle), predicate);
        nuint right = Standard::Collections::detail::StablePartitionHalves<T>(values.Slice(middle), predicate);
        nuint leftFalse = middle - left;
        if (leftFalse != 0 && right != 0)
            Standard::Collections::Rotate<T>(values.Slice(left, leftFalse + right), leftFalse);
        return left + right;
    }
}

// Groups matching elements first, preserving order within both groups. Returns the matching count.
// Tests each original element once in order. Predicate failure can leave a partial rearrangement.
// Uses O(n log n) relocations and O(log n) stack storage.
public nuint StablePartition<T>(Span<T> values, bool(immutable(T)*)* predicate)
    => Standard::Collections::detail::StablePartitionHalves<T>(values, predicate);
