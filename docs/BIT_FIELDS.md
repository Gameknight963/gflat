# Bit fields in byte buffers

`Standard::Binary::BitPrimitives` reads and writes unsigned fields of 0 through 64 bits
in borrowed byte buffers. It allocates nothing and requires no other Standard sources.

| API | Effect |
| --- | --- |
| `TryReadUInt64(data, count, bitOffset, bitCount, order, ref value)` | Reads a right-aligned `ulong`; failure sets `value` to zero. |
| `TryWriteUInt64(data, count, bitOffset, bitCount, order, value)` | Writes a value that fits the field, preserving every other bit. Failure leaves all bytes unchanged. |

`count` is a byte count; `bitOffset` counts bits from the buffer's start. Bytes
always advance in address order. `BitOrder::LeastSignificantFirst` visits bit 0
of each byte first, and the first visited bit becomes bit 0 of the value.
`BitOrder::MostSignificantFirst` visits bit 7 first, and the first visited bit
becomes the value's most significant requested bit. Results use only their low
`bitCount` bits. This choice controls both within-byte order and field significance.

```csharp
using Standard::Binary;

int main() nothrow
{
    byte[2] data = [(byte)0x96, (byte)0x35];
    ulong value = 0ul;
    if (!BitPrimitives::TryReadUInt64(data, 2, 4, 8, BitOrder::MostSignificantFirst, ref value)) return 1;
    if (value != 0x63ul) return 2;
    if (!BitPrimitives::TryWriteUInt64(data, 2, 4, 8, BitOrder::LeastSignificantFirst, 0xa5ul)) return 3;
    if (data[0] != (byte)0x56 || data[1] != (byte)0x3a) return 4;
    return 0;
}
```

Widths outside 0–64, invalid order values, and fields outside the buffer fail.
Writes also reject values with set bits above the requested width. A zero-width
operation succeeds only at a position inside the buffer or exactly at its end;
it touches no buffer bytes. A zero-width write accepts only zero. An unaligned
64-bit field can span nine bytes. Bounds checks avoid multiplying the byte count
by eight or adding the width to the bit offset.

Keep buffers alive and valid for their stated extent. Write buffers must be
readable as well as writable because writes preserve surrounding bits. The read
output must not overlap the input bytes. Operations are ordinary byte accesses;
they provide no synchronization. Time is bounded by 64 bit iterations, with
constant temporary storage. With `NoStdLib`, include only
`Standard/core/Binary/BitPrimitives.gf` alongside the caller.