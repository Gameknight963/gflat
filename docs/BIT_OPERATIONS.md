# Integer bit operations

`std::BitOperations` provides overloads for `uint` and `ulong`:

| Method | Result |
| --- | --- |
| `PopCount(value)` | Number of set bits. |
| `LeadingZeroCount(value)` | Number of zero bits before the highest set bit. |
| `TrailingZeroCount(value)` | Number of zero bits after the lowest set bit. |
| `RotateLeft(value, int offset)` | Rotate all bits left, wrapping bits around. |
| `RotateRight(value, int offset)` | Rotate all bits right, wrapping bits around. |
| `IsPowerOfTwo(value)` | True for a nonzero value with exactly one set bit. |

For zero, both zero-counting operations return the type's bit width: 32 or 64.
Rotations mask the offset to its low five or six bits, respectively. This defines
zero offsets, negative offsets, and offsets larger than the type width without
ever shifting by the full width. For example, rotating a uint left by -1 is the
same as rotating it right by 1.

`ReverseEndianness(value)` has fixed-width overloads for `short`, `ushort`,
`int`, `uint`, `long`, and `ulong`. It reverses the order of bytes within the
integer and returns that same type. Signed overloads preserve the bit pattern,
using two's-complement interpretation rather than a numeric sign conversion.
For example, `ReverseEndianness(0x12345678u)` is `0x78563412u`. Reversing twice
returns the original bits. This operation is independent of host byte order;
it does not read or write a buffer. There are no byte or pointer-sized overloads.

Every operation is `nothrow`, uses ordinary gflat integer operations, and requires
no allocation, libc, target-specific LLVM extension, or standard-library runtime
definitions. The implementation does not promise a particular CPU instruction;
LLVM may optimize these operations for the target.
