# Integer quotient and remainder

`Standard::Numerics::Math::DivRem(dividend, divisor, ref remainder)` returns the
integer quotient and writes the remainder. Division truncates toward zero, so
`DivRem(-17, 5, ref remainder)` returns `-3` and writes `-2`.

Overloads accept matching `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`,
`long`, `ulong`, `extralong`, `nint`, and `nuint` arguments.

```csharp
using Standard::Numerics;

int main()
{
    int remainder = 0;
    int quotient = Math::DivRem(-17, 5, ref remainder);
    if (quotient != -3 || remainder != -2) return 1;

    long pages = 0l;
    long leftover = 0l;
    if (!Math::TryDivRem(12345l, 4096l, ref pages, ref leftover)) return 2;
    if (pages != 3l || leftover != 57l) return 3;

    if (Math::TryDivRem(12345l, 0l, ref pages, ref leftover)) return 4;
    if (pages != 0l || leftover != 0l) return 5;
    return 0;
}
```

## Errors and output storage

`DivRem` throws `Exception("Division by zero")` when the divisor is zero, and
`Exception("Division overflow")` when a signed minimum is divided by `-1`.
Either error leaves the remainder unchanged.

`TryDivRem(dividend, divisor, ref quotient, ref remainder)` is `nothrow`. It
returns false for those same inputs and writes zero to both outputs. No
allocation is required.

Outputs may reuse either input variable. If both Try outputs refer to the same
variable, its final value is the remainder.
