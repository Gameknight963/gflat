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
