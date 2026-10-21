namespace std;

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
        int(readonly(T)*, readonly(T)*)* compare)
    {
        while (root < count / 2)
        {
            nuint child = root * 2 + 1;
            if (child + 1 < count && compare(data + child, data + child + 1) < 0) child++;
            if (compare(data + root, data + child) >= 0) return;
            std::detail::SwapElements<T>(data + root, data + child);
            root = child;
        }
    }
}

// Allocation-free heapsort. Equal elements need not retain their relative order.
// A throwing comparator can leave a partial ordering, but all values remain owned.
public void Sort<T>(Span<T> values, int(readonly(T)*, readonly(T)*)* compare)
{
    nuint count = values.Length;
    if (count < 2) return;
    T* data = values.Data!;
    nuint root = count / 2;
    while (root != 0)
    {
        root--;
        std::detail::SiftDown<T>(data, root, count, compare);
    }
    nuint end = count;
    while (end > 1)
    {
        end--;
        std::detail::SwapElements<T>(data, data + end);
        std::detail::SiftDown<T>(data, 0, end, compare);
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
        std::detail::SwapElements<T>(data + left, data + right);
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
    std::Reverse<T>(values.Slice(0, middle));
    std::Reverse<T>(values.Slice(middle));
    std::Reverse<T>(values);
}

// First index whose element is not less than key, or Length if none exists.
public nuint LowerBound<T>(Span<readonly(T)> values, readonly(T)* key,
    int(readonly(T)*, readonly(T)*)* compare)
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
public nuint UpperBound<T>(Span<readonly(T)> values, readonly(T)* key,
    int(readonly(T)*, readonly(T)*)* compare)
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

public bool IsSorted<T>(Span<readonly(T)> values, int(readonly(T)*, readonly(T)*)* compare)
{
    for (nuint i = 1; i < values.Length; i++)
        if (compare(&values[i - 1], &values[i]) > 0) return false;
    return true;
}

public bool SequenceEqual<T>(Span<readonly(T)> first, Span<readonly(T)> second,
    bool(readonly(T)*, readonly(T)*)* equal)
{
    if (first.Length != second.Length) return false;
    for (nuint i = 0; i < first.Length; i++)
        if (!equal(&first[i], &second[i])) return false;
    return true;
}

// Return Length for an empty span; strict comparisons keep the first equal item.
public nuint MinIndex<T>(Span<readonly(T)> values, int(readonly(T)*, readonly(T)*)* compare)
{
    if (values.Length == 0) return values.Length;
    nuint best = 0;
    for (nuint i = 1; i < values.Length; i++)
        if (compare(&values[i], &values[best]) < 0) best = i;
    return best;
}

public nuint MaxIndex<T>(Span<readonly(T)> values, int(readonly(T)*, readonly(T)*)* compare)
{
    if (values.Length == 0) return values.Length;
    nuint best = 0;
    for (nuint i = 1; i < values.Length; i++)
        if (compare(&values[i], &values[best]) > 0) best = i;
    return best;
}

// Preserve the first nonzero comparison; otherwise order by prefix length.
public int SequenceCompare<T>(Span<readonly(T)> first, Span<readonly(T)> second,
    int(readonly(T)*, readonly(T)*)* compare)
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

// Returns Length when no element matches, including for an empty span.
public nuint FindIndex<T>(Span<readonly(T)> values, bool(readonly(T)*)* predicate)
{
    for (nuint i = 0; i < values.Length; i++)
        if (predicate(&values[i])) return i;
    return values.Length;
}

// Search callbacks borrow both values; elements and keys are never copied.
public nuint IndexOf<T>(Span<readonly(T)> values, readonly(T)* key,
    bool(readonly(T)*, readonly(T)*)* equal)
{
    for (nuint i = 0; i < values.Length; i++)
        if (equal(&values[i], key)) return i;
    return values.Length;
}

public nuint LastIndexOf<T>(Span<readonly(T)> values, readonly(T)* key,
    bool(readonly(T)*, readonly(T)*)* equal)
{
    nuint i = values.Length;
    while (i != 0)
    {
        i--;
        if (equal(&values[i], key)) return i;
    }
    return values.Length;
}

public bool Contains<T>(Span<readonly(T)> values, readonly(T)* key,
    bool(readonly(T)*, readonly(T)*)* equal)
{
    return IndexOf<T>(values, key, equal) != values.Length;
}

// Move matching elements to the prefix and return its length. Not stable.
public nuint Partition<T>(Span<T> values, bool(readonly(T)*)* predicate)
{
    nuint boundary = 0;
    for (nuint i = 0; i < values.Length; i++)
    {
        if (predicate(&values[i]))
        {
            std::detail::SwapElements<T>(&values[i], &values[boundary]);
            boundary++;
        }
    }
    return boundary;
}
