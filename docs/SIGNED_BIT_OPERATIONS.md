# Bit operations on signed integers

`BitOperations` provides `PopCount`, `LeadingZeroCount`, `TrailingZeroCount`,
`RotateLeft`, and `RotateRight` for `int`, `long`, `nint`, and `extralong`.

```csharp
using Standard::Numerics;

int main()
{
    if (BitOperations::PopCount(-1) != 32) return 1;
    if (BitOperations::LeadingZeroCount(-1l) != 0) return 2;
    if (BitOperations::TrailingZeroCount((extralong)0) != 128) return 3;
    extralong top = (extralong)1 << 127;
    if (BitOperations::RotateLeft(top, 1) != (extralong)1) return 4;
    if (BitOperations::RotateRight((extralong)1, 1) != top) return 5;
    return 0;
}
```

## Width and sign

These operations inspect the fixed-width two's-complement representation, not
the absolute value. A negative value includes its sign bit in the count.
`PopCount(-1)` is 32 for `int`, 64 for `long`, and 128 for `extralong`.
Zero has a leading and trailing zero count equal to the type's width. `nint`
uses the target pointer width.

Rotations retain the input type and wrap offsets modulo that width. Negative
offsets rotate in the opposite direction. All `int` offsets, including its
minimum and maximum values, are supported. Rotation moves the sign bit like
any other bit and can therefore produce a negative result.

`IsPowerOfTwo` also accepts `extralong`, with the same signed rule as `int`,
`long`, and `nint`: only positive values with one set bit return true. A negative
minimum value has one set bit but is not a positive power of two.

All these methods are `nothrow`, allocate no storage, and need no wide-integer
division runtime. Include `Numerics/BitOperations.gf` for explicit source lists.
See [integer bit operations](BIT_OPERATIONS.md) for unsigned overloads and byte
order helpers.
