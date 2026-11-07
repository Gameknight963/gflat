# Scaling by powers of two

`Math::ScaleB(value, exponent)` computes `value * 2^exponent` for `float` and
`double`, using an integer exponent. It is available in
`Standard/core/Numerics/Math.gf`.

```csharp
using Standard::Numerics;

int main()
{
    if (Math::ScaleB(1.5, 3) != 12.0) return 1;
    if (Math::ScaleB(12.0f, -3) != 1.5f) return 2;
    double smallest = Math::ScaleB(1.0, -1074);
    if (smallest.reinterpret<ulong>() != 1ul) return 3;
    return 0;
}
```

Scaling is exact when the finite result remains a normal value. Subnormal
results round to nearest with ties to even. Overflow returns signed infinity;
underflow can return a subnormal or signed zero. Signed zero, NaN, and infinity
inputs are returned unchanged. Every signed `int` exponent is supported.

The implementation adjusts the encoded exponent and rounds the significand
with integer operations. It does not construct an overflowing or underflowing
floating multiplier. Both overloads are `nothrow`, allocate no storage, and
make no libc calls.
