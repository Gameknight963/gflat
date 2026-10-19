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

Every operation is `nothrow`, uses ordinary gflat integer operations, and requires
no allocation, libc, target-specific LLVM extension, or standard-library runtime
definitions. The implementation does not promise a particular CPU instruction;
LLVM may optimize these operations for the target.
