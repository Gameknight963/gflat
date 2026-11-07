namespace Standard::Memory;

// Tests byte-range overlap without reading elements. Empty spans never overlap.
public bool Overlaps<T>(Span<T> first, Span<T> second) nothrow
{
    if (first.IsEmpty || second.IsEmpty) return false;
    nuint left = (first.Data!).reinterpret<nuint>();
    nuint right = (second.Data!).reinterpret<nuint>();
    nuint stride = (nuint)sizeof(T);
    if (stride == 0) stride = 1;
    // Comparing distances avoids overflowing an end-address calculation.
    if (right >= left) return (right - left) / stride < first.Length;
    return (left - right) / stride < second.Length;
}

// Offset is measured in elements from first to second and is zero on failure.
public bool Overlaps<T>(Span<T> first, Span<T> second, ref nint elementOffset)
{
    elementOffset = 0;
    if (!Overlaps<T>(first, second)) return false;
    nuint left = (first.Data!).reinterpret<nuint>();
    nuint right = (second.Data!).reinterpret<nuint>();
    nuint distance = 0;
    if (right >= left) distance = right - left;
    else distance = left - right;
    nuint stride = (nuint)sizeof(T);
    if (stride == 0) stride = 1;
    if (distance % stride != 0) throw new Exception("Overlapping spans are not element-aligned");
    nuint elements = distance / stride;
    nuint maximum = ((nuint)0 - (nuint)1) / 2;
    nint result = 0;
    if (right < left)
    {
        if (elements > maximum + 1) throw new Exception("Span element offset is outside native signed range");
        result = (nint)((nuint)0 - elements);
    }
    else
    {
        if (elements > maximum) throw new Exception("Span element offset is outside native signed range");
        result = (nint)elements;
    }
    elementOffset = result;
    return true;
}

extension Span<T>
{
    public readonly bool Overlaps(Span<T> other) nothrow
        => Standard::Memory::Overlaps<T>(*this, other);

    public readonly bool Overlaps(Span<T> other, ref nint elementOffset)
        => Standard::Memory::Overlaps<T>(*this, other, ref elementOffset);
}
