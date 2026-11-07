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

extension Span<T>
{
    public readonly bool Overlaps(Span<T> other) nothrow
        => Standard::Memory::Overlaps<T>(*this, other);
}
