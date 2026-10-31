# Scalar math

`Standard::Numerics::Math` supplies integer and floating-point operations.

## Integers

`Min`, `Max`, and `Clamp` have overloads for `sbyte`, `byte`, `short`, `ushort`,
`int`, `uint`, `long`, `ulong`, `extralong`, `nint`, and `nuint`. Results keep
the argument type. Clamp includes both bounds and throws if minimum exceeds
maximum. Signed Abs throws for the type's minimum value, whose positive
counterpart does not fit.

### Common divisors and multiples

GCD and LCM accept matching `uint`, `ulong`, or `nuint` arguments.

```csharp
using Standard::Numerics;

uint divisor = Math::GreatestCommonDivisor(48u, 18u); // 6
uint period = 0u;
bool fits = Math::TryLeastCommonMultiple(6u, 8u, ref period); // true, 24
```

- `GreatestCommonDivisor(0, value)` returns `value`, and GCD of two zeroes is zero
- `TryLeastCommonMultiple(a, b, ref result)` returns false and writes zero on
  overflow. Either zero input succeeds with zero
- `LeastCommonMultiple(a, b)` throws `Exception("Least common multiple overflow")`
  when the result does not fit

LCM divides out the common factor before multiplying, so only the final result
must fit. The Try output can be the same variable as either input.

```csharp
using Standard;
using Standard::Numerics;

int main()
{
    uint period = Math::LeastCommonMultiple(6u, 8u);
    ulong cycle = Math::LeastCommonMultiple(1000ul, 1500ul);
    if (period != 24u || cycle != 3000ul) return 1;
    nuint ticks = Math::LeastCommonMultiple((nuint)12, (nuint)18);
    if (ticks != (nuint)36) return 2;
    return 0;
}
```

## Floating point

Both `float` and `double` support the following `nothrow` methods:

| Method | Behavior |
| --- | --- |
| `IsNaN`, `IsFinite` | Classify values, with finite including zeros and subnormals |
| `Abs` | Clear the sign bit, including on zero and NaNs |
| `CopySign(magnitude, sign)` | Replace the sign bit and preserve all other bits |
| `Min`, `Max` | Select by numeric order with the NaN and zero rules below |
| `BitIncrement`, `BitDecrement` | Step to an adjacent representable value |
| `Sqrt` | Compute square root |

### NaNs and signed zero

Min and Max return a NaN input. If both are NaN, the first is returned.
For opposite signed zeroes, Min selects negative zero and Max positive zero.

CopySign preserves NaN payloads and signaling bits. Negative zero or a negative
NaN supplies a negative sign.

### Adjacent values

```csharp
using Standard::Numerics;

float above = Math::BitIncrement(1.0f); // Smallest float greater than 1.
double below = Math::BitDecrement(1.0); // Largest double less than 1.
```

BitIncrement steps toward positive infinity and BitDecrement toward negative
infinity. Both signs of zero increment to the smallest positive subnormal and
decrement to the smallest negative subnormal. A step back retains the
corresponding zero sign.

Stepping outward from infinity keeps that infinity. Stepping inward returns
the largest finite value of the same sign. NaNs keep their exact bits.

### Integral rounding

These methods return the same floating-point type:

| Method | Result |
| --- | --- |
| `Truncate` | Integral value toward zero |
| `Floor` | Greatest integral value at most the input |
| `Ceiling` | Least integral value at least the input |
| `Round` | Nearest integral value, choosing the even integer at a tie |

For example, `Round(2.5)` is `2.0`, `Round(3.5)` is `4.0`, and `Round(-0.5)`
is negative zero. Truncate and Ceiling also retain negative zero for small
negative inputs. Already integral values, infinities, and NaNs keep their bits.

### Square root

Sqrt preserves either zero sign and positive infinity. Negative nonzero inputs
produce NaN. NaN inputs produce NaN with an unspecified sign and payload.

The implementation uses [LLVM square-root intrinsics](https://releases.llvm.org/22.1.0/docs/LangRef.html#llvm-sqrt-intrinsic).
They use LLVM's ordinary floating-point environment assumptions. They do not
provide C errno reporting or floating-point trap control.

## Source dependencies

`core/Numerics/Math.gf` also declares checked integer operations. Selective
NoStdLib builds must supply their exception and Memory contracts, including
when only a floating-point helper is used.
