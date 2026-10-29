using std::Memory;
namespace std::Collections;

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
        nuint left = std::Collections::detail::StablePartitionHalves<T>(values.Slice(0, middle), predicate);
        nuint right = std::Collections::detail::StablePartitionHalves<T>(values.Slice(middle), predicate);
        nuint leftFalse = middle - left;
        if (leftFalse != 0 && right != 0)
            std::Collections::Rotate<T>(values.Slice(left, leftFalse + right), leftFalse);
        return left + right;
    }
}

/// Stable matching prefix and nonmatching suffix; returns the matching count.
/// Calls predicate once per original element, in original left-to-right order.
/// No heap scratch or element copies; O(n log n) relocations and O(log n) stack.
/// Predicate exceptions can leave a partial rearrangement; all elements remain owned.
public nuint StablePartition<T>(Span<T> values, bool(immutable(T)*)* predicate)
    => std::Collections::detail::StablePartitionHalves<T>(values, predicate);
