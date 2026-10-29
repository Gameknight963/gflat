using std::Memory;
namespace std::Collections;

namespace detail
{
    // Relocate rather than copy: noncopyable owning elements are supported.
    private void SwapElements<T>(T* left, T* right)
    {
        if (left == right) return;
        T temporary = move *left;
        move *right at left;
        move temporary at right;
    }

    private void SiftDown<T>(T* data, nuint root, nuint count,
        int(immutable(T)*, immutable(T)*)* compare)
    {
        while (root < count / 2)
        {
            nuint child = root * 2 + 1;
            if (child + 1 < count && compare(data + child, data + child + 1) < 0) child++;
            if (compare(data + root, data + child) >= 0) return;
            std::Collections::detail::SwapElements<T>(data + root, data + child);
            root = child;
        }
    }
}

// Exchange initialized values without copying their owned resources.
public void Swap<T>(ref T left, ref T right)
    => std::Collections::detail::SwapElements<T>(&left, &right);

// Allocation-free heapsort. Equal elements need not retain their relative order.
// A throwing comparator can leave a partial ordering, but all values remain owned.
public void Sort<T>(Span<T> values, int(immutable(T)*, immutable(T)*)* compare)
{
    nuint count = values.Length;
    if (count < 2) return;
    T* data = values.Data!;
    nuint root = count / 2;
    while (root != 0)
    {
        root--;
        std::Collections::detail::SiftDown<T>(data, root, count, compare);
    }
    nuint end = count;
    while (end > 1)
    {
        end--;
        std::Collections::detail::SwapElements<T>(data, data + end);
        std::Collections::detail::SiftDown<T>(data, 0, end, compare);
    }
}

public void Reverse<T>(Span<T> values)
{
    if (values.Length < 2) return;
    T* data = values.Data!;
    nuint left = 0;
    nuint right = values.Length - 1;
    while (left < right)
    {
        std::Collections::detail::SwapElements<T>(data + left, data + right);
        left++;
        right--;
    }
}

// Move the prefix [0, middle) after the suffix, preserving order within each.
// Validate before relocating any element; both endpoints are no-ops.
public void Rotate<T>(Span<T> values, nuint middle)
{
    if (middle > values.Length) throw new Exception("Rotation split out of range", 3);
    if (middle == 0 || middle == values.Length) return;
    std::Collections::Reverse<T>(values.Slice(0, middle));
    std::Collections::Reverse<T>(values.Slice(middle));
    std::Collections::Reverse<T>(values);
}

// First index whose element is not less than key, or Length if none exists.
public nuint LowerBound<T>(Span<immutable(T)> values, immutable(T)* key,
    int(immutable(T)*, immutable(T)*)* compare)
{
    nuint first = 0;
    nuint count = values.Length;
    while (count != 0)
    {
        nuint step = count / 2;
        nuint middle = first + step;
        if (compare(&values[middle], key) < 0)
        {
            first = middle + 1;
            count -= step + 1;
        }
        else count = step;
    }
    return first;
}

// First index whose element is greater than key, or Length if none exists.
public nuint UpperBound<T>(Span<immutable(T)> values, immutable(T)* key,
    int(immutable(T)*, immutable(T)*)* compare)
{
    nuint first = 0;
    nuint count = values.Length;
    while (count != 0)
    {
        nuint step = count / 2;
        nuint middle = first + step;
        if (compare(&values[middle], key) <= 0)
        {
            first = middle + 1;
            count -= step + 1;
        }
        else count = step;
    }
    return first;
}

/// Borrows the contiguous comparer-equivalent range in an already sorted span.
/// Use a consistent ordering; empty results have null Data and zero Length.
/// Elements and key are not copied; comparator exceptions propagate.
public Span<immutable(T)> EqualRange<T>(Span<immutable(T)> values, immutable(T)* key,
    int(immutable(T)*, immutable(T)*)* compare)
{
    nuint first = std::Collections::LowerBound<T>(values, key, compare);
    nuint last = std::Collections::UpperBound<T>(values, key, compare);
    return values.Slice(first, last - first);
}

/// Checks that all true predicate results precede all false results.
/// Visits borrowed elements once from left to right, stopping at the first violation.
/// Empty spans return true without calling the predicate; predicate exceptions propagate.
public bool IsPartitioned<T>(Span<immutable(T)> values, bool(immutable(T)*)* predicate)
{
    bool seenFalse = false;
    for (nuint i = 0; i < values.Length; i++)
    {
        bool matches = predicate(&values[i]);
        if (!matches) seenFalse = true;
        else if (seenFalse) return false;
    }
    return true;
}

/// Finds the first false predicate result in an already partitioned span.
/// True results must precede false results; keep the predicate consistent.
/// Returns Length when all match. Empty spans never call the predicate.
/// Borrows elements without allocation or copying; predicate exceptions propagate.
public nuint PartitionPoint<T>(Span<immutable(T)> values, bool(immutable(T)*)* predicate)
{
    nuint first = 0;
    nuint count = values.Length;
    while (count != 0)
    {
        nuint step = count / 2;
        nuint middle = first + step;
        if (predicate(&values[middle]))
        {
            first = middle + 1;
            count -= step + 1;
        }
        else count = step;
    }
    return first;
}

// First matching index in sorted input, or Length when the key is absent.
// Use the same ordering as Sort; neither elements nor the key are copied.
public nuint BinarySearch<T>(Span<immutable(T)> values, immutable(T)* key,
    int(immutable(T)*, immutable(T)*)* compare)
{
    nuint index = std::Collections::LowerBound<T>(values, key, compare);
    if (index == values.Length) return values.Length;
    if (compare(&values[index], key) != 0) return values.Length;
    return index;
}

public bool IsSorted<T>(Span<immutable(T)> values, int(immutable(T)*, immutable(T)*)* compare)
{
    for (nuint i = 1; i < values.Length; i++)
        if (compare(&values[i - 1], &values[i]) > 0) return false;
    return true;
}

/// First element that breaks nondecreasing order, or Length if every pair is ordered.
/// Compares borrowed previous/current elements left to right; empty and singleton spans make no calls.
/// Does not copy elements or allocate storage; comparator exceptions propagate.
public nuint FindUnsortedIndex<T>(Span<immutable(T)> values, int(immutable(T)*, immutable(T)*)* compare)
{
    for (nuint i = 1; i < values.Length; i++)
        if (compare(&values[i - 1], &values[i]) > 0) return i;
    return values.Length;
}

/// Tests sorted multiset inclusion, respecting comparison-equivalent duplicate counts.
/// Both spans must use the same consistent ordering; overlapping spans are permitted.
/// Comparator receives source then required; no element copies or library allocation.
public bool Includes<T>(Span<immutable(T)> values, Span<immutable(T)> required,
    int(immutable(T)*, immutable(T)*)* compare)
{
    if (required.Length == 0) return true;
    if (required.Length > values.Length) return false;
    nuint first = 0; nuint second = 0;
    while (second < required.Length)
    {
        if (first == values.Length) return false;
        int order = compare(&values[first], &required[second]);
        if (order > 0) return false;
        if (order == 0) second++;
        first++;
    }
    return true;
}

public bool SequenceEqual<T>(Span<immutable(T)> first, Span<immutable(T)> second,
    bool(immutable(T)*, immutable(T)*)* equal)
{
    if (first.Length != second.Length) return false;
    for (nuint i = 0; i < first.Length; i++)
        if (!equal(&first[i], &second[i])) return false;
    return true;
}

/// Tests the leading borrowed elements; an empty prefix matches and an oversized prefix reads nothing.
public bool StartsWith<T>(Span<immutable(T)> values, Span<immutable(T)> prefix,
    bool(immutable(T)*, immutable(T)*)* equal)
{
    if (prefix.Length > values.Length) return false;
    return std::Collections::SequenceEqual<T>(values.Slice(0, prefix.Length), prefix, equal);
}

/// Tests the trailing borrowed elements from left to right, without copying or changing storage.
public bool EndsWith<T>(Span<immutable(T)> values, Span<immutable(T)> suffix,
    bool(immutable(T)*, immutable(T)*)* equal)
{
    if (suffix.Length > values.Length) return false;
    return std::Collections::SequenceEqual<T>(values.Slice(values.Length - suffix.Length, suffix.Length), suffix, equal);
}

/// Counts matching corresponding borrowed elements, stopping at the first difference or shorter length.
public nuint CommonPrefixLength<T>(Span<immutable(T)> first, Span<immutable(T)> second,
    bool(immutable(T)*, immutable(T)*)* equal)
{
    nuint count = first.Length;
    if (second.Length < count) count = second.Length;
    for (nuint i = 0; i < count; i++)
        if (!equal(&first[i], &second[i])) return i;
    return count;
}

// Return Length for an empty span; strict comparisons keep the first equal item.
public nuint MinIndex<T>(Span<immutable(T)> values, int(immutable(T)*, immutable(T)*)* compare)
{
    if (values.Length == 0) return values.Length;
    nuint best = 0;
    for (nuint i = 1; i < values.Length; i++)
        if (compare(&values[i], &values[best]) < 0) best = i;
    return best;
}

public nuint MaxIndex<T>(Span<immutable(T)> values, int(immutable(T)*, immutable(T)*)* compare)
{
    if (values.Length == 0) return values.Length;
    nuint best = 0;
    for (nuint i = 1; i < values.Length; i++)
        if (compare(&values[i], &values[best]) > 0) best = i;
    return best;
}

// Preserve the first nonzero comparison; otherwise order by prefix length.
public int SequenceCompare<T>(Span<immutable(T)> first, Span<immutable(T)> second,
    int(immutable(T)*, immutable(T)*)* compare)
{
    nuint count = first.Length;
    if (second.Length < count) count = second.Length;
    for (nuint i = 0; i < count; i++)
    {
        int order = compare(&first[i], &second[i]);
        if (order != 0) return order;
    }
    if (first.Length < second.Length) return -1;
    if (first.Length > second.Length) return 1;
    return 0;
}

/// Tests borrowed elements from left to right, stopping at the first false result.
/// Empty spans return true without calling the predicate; exceptions propagate.
public bool AllOf<T>(Span<immutable(T)> values, bool(immutable(T)*)* predicate)
{
    for (nuint i = 0; i < values.Length; i++)
        if (!predicate(&values[i])) return false;
    return true;
}

/// Tests borrowed elements from left to right, stopping at the first true result.
/// Empty spans return false without calling the predicate; exceptions propagate.
public bool AnyOf<T>(Span<immutable(T)> values, bool(immutable(T)*)* predicate)
    => std::Collections::FindIndex<T>(values, predicate) != values.Length;

/// Tests borrowed elements from left to right, stopping at the first true result.
/// Returns true when none match, including empty spans; exceptions propagate.
public bool NoneOf<T>(Span<immutable(T)> values, bool(immutable(T)*)* predicate)
    => std::Collections::FindIndex<T>(values, predicate) == values.Length;

/// Counts matches while borrowing each element once from left to right.
/// Empty spans return zero without calling the predicate; exceptions propagate.
public nuint CountIf<T>(Span<immutable(T)> values, bool(immutable(T)*)* predicate)
{
    nuint count = 0;
    for (nuint i = 0; i < values.Length; i++)
        if (predicate(&values[i])) count++;
    return count;
}

// Returns Length when no element matches, including for an empty span.
public nuint FindIndex<T>(Span<immutable(T)> values, bool(immutable(T)*)* predicate)
{
    for (nuint i = 0; i < values.Length; i++)
        if (predicate(&values[i])) return i;
    return values.Length;
}

// First matching adjacent pair's left index, or Length if no pair matches.
// Borrow both elements, visiting pairs left to right until the first match.
public nuint AdjacentFind<T>(Span<immutable(T)> values,
    bool(immutable(T)*, immutable(T)*)* predicate)
{
    for (nuint i = 1; i < values.Length; i++)
        if (predicate(&values[i - 1], &values[i])) return i - 1;
    return values.Length;
}

// Returns Length when no element matches, including for an empty span.
public nuint FindLastIndex<T>(Span<immutable(T)> values, bool(immutable(T)*)* predicate)
{
    nuint index = values.Length;
    while (index != 0)
    {
        index--;
        if (predicate(&values[index])) return index;
    }
    return values.Length;
}

// Search callbacks borrow both values; elements and keys are never copied.
public nuint IndexOf<T>(Span<immutable(T)> values, immutable(T)* key,
    bool(immutable(T)*, immutable(T)*)* equal)
{
    for (nuint i = 0; i < values.Length; i++)
        if (equal(&values[i], key)) return i;
    return values.Length;
}

public nuint LastIndexOf<T>(Span<immutable(T)> values, immutable(T)* key,
    bool(immutable(T)*, immutable(T)*)* equal)
{
    nuint i = values.Length;
    while (i != 0)
    {
        i--;
        if (equal(&values[i], key)) return i;
    }
    return values.Length;
}

public bool Contains<T>(Span<immutable(T)> values, immutable(T)* key,
    bool(immutable(T)*, immutable(T)*)* equal)
{
    return IndexOf<T>(values, key, equal) != values.Length;
}

// Move matching elements to the prefix and return its length. Not stable.
public nuint Partition<T>(Span<T> values, bool(immutable(T)*)* predicate)
{
    nuint boundary = 0;
    for (nuint i = 0; i < values.Length; i++)
    {
        if (predicate(&values[i]))
        {
            std::Collections::detail::SwapElements<T>(&values[i], &values[boundary]);
            boundary++;
        }
    }
    return boundary;
}
