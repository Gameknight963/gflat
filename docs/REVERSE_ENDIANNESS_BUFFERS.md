# Reversing byte order in buffers

`BinaryPrimitives::TryReverseEndianness(source, count, destination, capacity)`
copies integers while reversing each integer's byte order. Counts and
capacities are in elements, not bytes. The overloads cover the same 11 integer
types as scalar `ReverseEndianness`.

Load `Standard/core/Binary/reverse_endianness_span.gf` for the freestanding
`ReverseEndianness(Span<readonly(T)> source, Span<T> destination)` wrapper.
Its element type must be one of the supported integer types.

```csharp
using Standard::Binary;
using Standard::Memory;

int main()
{
    uint[3] values = [0x01020304u, 0x11223344u, 0xaabbccddu];
    Span<uint> buffer = new(&values[0], 3);
    ReverseEndianness(buffer.Slice(0, 2).AsReadOnly(), buffer.Slice(1, 2));
    if (values[0] != 0x01020304u) return 1;
    if (values[1] != 0x04030201u || values[2] != 0x44332211u) return 2;
    return 0;
}
```

Source and destination may overlap, including the exact same range. Results
are as if the original source elements were read before replacement. The
implementation traverses in the appropriate direction without allocating a
snapshot. Both ranges must designate correctly aligned elements of the same
type. The destination suffix beyond the source length remains unchanged.

An insufficient destination returns false from the pointer/count form, which
is `nothrow`. The span wrapper throws before any writes. An empty source touches
no buffer storage. The pointer/count form returns true after a complete conversion.
Successful operations allocate no storage and take linear
time in the source element count. Reversal changes byte positions within each
integer and preserves the order of integers in the sequence.
