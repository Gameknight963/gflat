# Integer bit operations

`Standard::Numerics::BitOperations` has `uint` and `ulong` overloads:

| Method | Result |
| --- | --- |
| `PopCount` | Number of set bits |
| `LeadingZeroCount` | Zero bits before the highest set bit |
| `TrailingZeroCount` | Zero bits after the lowest set bit |
| `RotateLeft(value, offset)` | Rotate bits left with wraparound |
| `RotateRight(value, offset)` | Rotate bits right with wraparound |
| `IsPowerOfTwo` | True for a nonzero value with one set bit |
| `RoundUpToPowerOfTwo` | Smallest representable power at least as large as the input |

For zero, both zero-counting methods return the type's bit width. Rotation
offsets wrap modulo 32 or 64, including negative offsets. Rotating a uint left
by -1 is equivalent to rotating it right by 1.

RoundUpToPowerOfTwo returns zero for zero or overflow. Exact powers stay
unchanged. For capacity calculations, check that a nonzero request produced
a nonzero result before allocating.

## Byte order

`ReverseEndianness` accepts `short`, `ushort`, `int`, `uint`, `long`, and `ulong`.
It reverses byte order while retaining the type and bit pattern. For example,
`0x12345678u` becomes `0x78563412u`. Signed results use two's-complement
interpretation. The operation is independent of host byte order.

All methods are `nothrow`. Source: `core/Numerics/BitOperations.gf`.
