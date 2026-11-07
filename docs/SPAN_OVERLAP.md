# Checking span overlap

Load `Memory/span_overlap.gf` for `Span<T>.Overlaps(other)` or the free
`Standard::Memory::Overlaps(first, second)` function. The result is true when
nonempty spans share at least one byte of storage. Adjacent ranges do not overlap.

```csharp
using Standard::Memory;

int main()
{
    int[4] buffer = [1, 2, 3, 4];
    Span<int> all = new(&buffer[0], 4);
    if (!all.Slice(0, 2).Overlaps(all.Slice(1, 2))) return 1;
    if (all.Slice(0, 2).Overlaps(all.Slice(2, 2))) return 2;
    return 0;
}
```

Both spans use the same element type, including any element qualification.
For mutable and readonly views, convert both with `AsReadOnly()` before comparing.
The check is `nothrow`, takes constant time, allocates nothing, and reads no
elements. It compares address distances without forming potentially overflowing
end addresses. Empty spans never overlap, including empty slices of the same
buffer.

The result does not prove that backing storage is valid or alive. Callers remain
responsible for the storage described by their spans.
