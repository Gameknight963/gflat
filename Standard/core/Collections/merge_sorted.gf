using Standard::Memory;

namespace Standard::Collections;

// Copies two sorted inputs into a new owning vector. Equivalent elements from
// first precede those from second. Element copying is required when called.
public Vector<T> MergeSorted<T>(Span<immutable(T)> first, Span<immutable(T)> second,
    int(immutable(T)*, immutable(T)*)* compare)
{
    nuint maximum = (nuint)0 - (nuint)1;
    if (first.Length > maximum - second.Length) throw new Exception("Merged sequence length overflow");
    Vector<T> result = new();
    result.Reserve(first.Length + second.Length);
    nuint left = 0;
    nuint right = 0;
    while (left < first.Length && right < second.Length)
    {
        if (compare(&first[left], &second[right]) <= 0)
        {
            result.Add(first[left]);
            left++;
        }
        else
        {
            result.Add(second[right]);
            right++;
        }
    }
    while (left < first.Length)
    {
        result.Add(first[left]);
        left++;
    }
    while (right < second.Length)
    {
        result.Add(second[right]);
        right++;
    }
    return move result;
}
