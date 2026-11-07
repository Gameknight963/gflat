# Native-width bit operations

`BitOperations` provides `nuint` overloads for `PopCount`, `LeadingZeroCount`,
`TrailingZeroCount`, `RotateLeft`, `RotateRight`, `IsPowerOfTwo`, and
`RoundUpToPowerOfTwo`, alongside the existing native `Log2`.
Load `Standard/core/Numerics/BitOperations.gf`.

```csharp
using Standard::Numerics;

int main()
{
    nuint mask = (nuint)0x81;
    if (BitOperations::PopCount(mask) != 2) return 1;
    if (BitOperations::TrailingZeroCount((nuint)0) != (int)sizeof(nuint) * 8) return 2;
    if (BitOperations::RoundUpToPowerOfTwo((nuint)129) != (nuint)256) return 3;
    if (BitOperations::IsPowerOfTwo(-8) || !BitOperations::IsPowerOfTwo(8)) return 4;
    return 0;
}
```

Counts and rotations use the target native integer width. Zero has a leading
and trailing zero count equal to that width. Rotation offsets wrap modulo the
width, including negative offsets and the smallest signed `int` offset.
Rounding up returns zero for zero or when the next power cannot fit in `nuint`.

Signed `IsPowerOfTwo` overloads accept `int`, `long`, and `nint`. Only positive
powers return true. A signed minimum value is false despite having a single
set bit in its representation. All these operations are `nothrow` and require
no allocation or libc support.
