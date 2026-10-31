# Bit fields in byte buffers

`Standard::Binary::BitPrimitives` reads and writes unsigned fields up to 64 bits.
`count` measures buffer bytes. `bitOffset` measures bits from the start.

| API | Result |
| --- | --- |
| `TryReadUInt64(data, count, bitOffset, bitCount, order, ref value)` | Reads into the low `bitCount` bits of `value`. Failure writes zero. |
| `TryWriteUInt64(data, count, bitOffset, bitCount, order, value)` | Writes the field and preserves surrounding bits. Failure preserves the buffer. |

Bytes advance in address order. The order selects which bit within each byte
comes first and how that bit contributes to the result:

- `LeastSignificantFirst`: starts with bit 0, which becomes result bit 0
- `MostSignificantFirst`: starts with bit 7, which becomes the result's highest requested bit

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

Operations fail for a width outside 0–64, an invalid order, or a field outside the
buffer. Writes also fail when the value has bits set above the field width.

A zero-width field is valid through the buffer's end. Its write value must be
zero. An unaligned 64-bit field can span nine bytes. Write buffers must be
readable because writes preserve surrounding bits. Read output must be separate
from the input buffer.

Source: `core/Binary/BitPrimitives.gf`
