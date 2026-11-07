# Integer bit operations

`Standard::Numerics::BitOperations` has `uint` and `ulong` overloads:

| Method | Result |
| --- | --- |
| `PopCount` | Number of set bits |
| `Log2` | Floor of the base-two logarithm, returning zero for zero |
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

`Log2` also accepts `nuint`. See [integer base-two logarithms](INTEGER_LOGARITHMS.md)
for highest-set-bit indexing and floating-point exponent extraction.

## Byte order

`ReverseEndianness` accepts `short`, `ushort`, `int`, `uint`, `long`, and `ulong`.
It reverses byte order while retaining the type and bit pattern. For example,
`0x12345678u` becomes `0x78563412u`. Signed results use two's-complement
interpretation. The operation is independent of host byte order.

All methods are `nothrow`. Source: `core/Numerics/BitOperations.gf`.

See [Native-width bit operations](NATIVE_BIT_OPERATIONS.md) for pointer-sized masks and signed power checks.

Signed counts and rotations also accept `int`, `long`, `nint`, and `extralong`.
See [signed bit operations](SIGNED_BIT_OPERATIONS.md) for two's-complement
semantics and 128-bit examples.
