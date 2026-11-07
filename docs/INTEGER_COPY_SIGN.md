# Copying integer signs

`Math::CopySign(value, sign)` returns the magnitude of `value` with the sign of
`sign`. A zero sign requests a nonnegative result. Signed overloads cover
`sbyte`, `short`, `int`, `long`, `nint`, and `extralong`.

```csharp
using Standard::Numerics;

int main()
{
    if (Math::CopySign(-12, 0) != 12) return 1;
    if (Math::CopySign(12, -7) != -12) return 2;
    long minimum = -9223372036854775807l - 1l;
    if (Math::CopySign(minimum, -1l) != minimum) return 3;
    return 0;
}
```

The minimum signed value cannot be made positive. Passing that value with a
nonnegative sign throws `Exception`. If used in an assignment, the destination
retains its previous value when this error occurs. Other inputs need no
allocation. Floating-point overloads preserve sign bits, including negative zero,
and do not have this integer overflow case.

The integer behavior follows [.NET CopySign](https://learn.microsoft.com/en-us/dotnet/api/system.int32.copysign).
