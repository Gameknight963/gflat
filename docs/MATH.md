# Scalar math

`std/core/Numerics/Math.gf` provides `std::Math`, with scalar helpers and checked integer
operations. It does not directly call C math functions. The checked operations
still require the normal exception and allocation support.

## Integers

`Min(a, b)`, `Max(a, b)`, and `Clamp(value, minimum, maximum)` have ordinary
overloads for `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`,
`extralong`, `nint`, and `nuint`. Each returns the same integer type
as its arguments, including exact signed 128-bit comparisons without
conversion through floating point.
`Clamp` includes both bounds and throws `Exception` if minimum exceeds maximum.
Use matching argument types; these APIs do not define mixed signed/unsigned
comparison rules of their own.

Every `Min` and `Max` overload is `nothrow`. `Clamp` retains its throwing
contract for inverted bounds.

`Abs(value)` has overloads for the signed types. The most negative value has no
positive counterpart in the same type, so it throws `Exception` rather than
wrapping or invoking signed overflow. This check also applies to pointer-sized
`nint` and 128-bit `extralong` values.

### Common divisors and multiples

`GreatestCommonDivisor(a, b)` and `TryLeastCommonMultiple(a, b, ref result)`
have `uint`, `ulong`, and `nuint` overloads. Use matching unsigned argument and
result types; no signed absolute-value convention is implied.

GCD returns the greatest common divisor using Euclid's algorithm.
`GreatestCommonDivisor(0, value)` returns `value`, and GCD of two zeroes is zero.
LCM returns true with the exact least common multiple when it fits. If either
operand is zero it succeeds with zero; overflow returns false and sets `result`
to zero. It divides out the GCD before checking multiplication, so a large
intermediate `a * b` does not itself cause rejection.

```csharp
uint divisor = Math::GreatestCommonDivisor(48u, 18u); // 6
uint period = 0u;
bool fits = Math::TryLeastCommonMultiple(6u, 8u, ref period); // true, 24
```

For `TryLeastCommonMultiple`, the result may alias a caller variable passed as
either input: input values are captured before the result is written. The GCD
and Try overloads are `nothrow`, allocate nothing, and add no library or runtime
dependencies. Existing throwing methods in `Math.gf` still require their normal
exception support.

`LeastCommonMultiple(a, b)` is the checked value-returning companion, with the
same `uint`, `ulong`, and `nuint` overloads. It returns the exact value when it
fits, including zero if either operand is zero, and throws
`Exception("Least common multiple overflow")` otherwise. It delegates to the
same checked calculation as `TryLeastCommonMultiple`, so a common factor is
removed before multiplication. Successful calls allocate nothing; throwing uses
ordinary exception support. These overloads are not `nothrow`.

Use the value-returning form when a combined period must fit, or the Try form
when overflow is an expected outcome:

```csharp
using std;
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

`float` and `double` each have `Sqrt`, `CopySign`, `BitIncrement`, `BitDecrement`,
`IsNaN`, `IsFinite`, `Abs`, `Min`, `Max`, `Truncate`, `Floor`, `Ceiling`, and `Round` overloads.
`IsFinite` excludes both infinities and all NaNs, and includes zeros
and subnormals. `Abs` clears the sign bit, including for negative zero and NaNs.
All floating-point helpers are `nothrow`; the signed-integer `Abs` overloads
retain their overflow exceptions.

`CopySign(magnitude, sign)` returns the first argument with its sign bit replaced
by the second argument's sign bit. It preserves every other bit, including NaN
payloads and signaling bits. Negative zero and negative NaNs supply a negative
sign even though comparing them with zero would not detect it. Both overloads
use ordinary integer bit operations and allocate nothing.

`BitIncrement(value)` returns the next representable value toward positive
infinity; `BitDecrement(value)` steps toward negative infinity. These are not
additions or subtractions of a fixed epsilon: spacing changes with magnitude.
Both positive and negative zero increment to the smallest positive subnormal
and decrement to the smallest negative subnormal. A step back from those
subnormals retains the corresponding zero sign.

Positive infinity stays unchanged when incremented; negative infinity stays
unchanged when decremented. Stepping either infinity in the opposite direction
returns the largest finite value of that sign. NaNs retain their exact bits,
including sign, payload and signaling bit. Both operations are `nothrow`, use
integer bit operations and add no runtime or C math dependency.

```csharp
float above = Math::BitIncrement(1.0f); // Smallest float greater than 1.
double below = Math::BitDecrement(1.0); // Largest double less than 1.
```

`Min` and `Max` propagate a NaN input rather than silently selecting the numeric
input. When both inputs are NaN the first is returned. When comparing positive
and negative zero, `Min` chooses negative zero and `Max` chooses positive zero,
independently of argument order. Equal zeros of the same sign retain that sign.
Other finite and infinite values use ordinary comparisons.

Classification, `Abs`, `Min`, and `Max` inspect the language's IEEE
binary32/binary64 values using ordinary gflat operations and pointer casts.
They allocate nothing on their normal paths.

### Integral rounding

These functions return the same floating-point type as their input; they do not
convert to an integer type or impose an integer range limit:

| Method | Result |
| --- | --- |
| `Truncate(value)` | Integral value toward zero. |
| `Floor(value)` | Greatest integral value no greater than the input. |
| `Ceiling(value)` | Least integral value no less than the input. |
| `Round(value)` | Nearest integral value, with exact halfway cases choosing the even integer. |

For example, `Round(2.5)` is `2.0`, `Round(3.5)` is `4.0`, and `Round(-0.5)`
is negative zero. `Truncate(-0.25)` and `Ceiling(-0.25)` also return negative
zero, whereas `Floor(-0.25)` returns `-1.0`. Every method preserves an input's
zero sign. Already integral values, infinities, and NaNs are returned unchanged;
NaN signs, payloads, and signaling bits are preserved in the returned value.

The implementation uses IEEE binary32/binary64 bit masks. `Floor` and `Ceiling`
adjust a truncated value by an exactly representable one only when required;
`Round` examines discarded bits and the retained integer's parity. All eight
overloads are `nothrow`, allocate nothing, and use no C math functions, LLVM
rounding intrinsics, or floating-to-integer conversions. They do not offer
decimal-digit rounding, alternative midpoint modes, or control over floating-
point environment flags and traps. The whole-source runtime caveat below still
applies to `Math.gf`.

### Square root

`Sqrt(float value)` returns `float`; `Sqrt(double value)` returns `double`.
Both are `nothrow`. Positive finite inputs produce the IEEE square root;
positive infinity remains positive infinity, and both signs of zero retain
their sign. Negative nonzero inputs, including negative infinity, produce NaN.
NaN inputs produce NaN; their exact payload, sign, and signaling state are not
promised for this arithmetic operation.

The implementation uses existing `__llvm_module` support to call
`llvm.sqrt.f32` / `llvm.sqrt.f64`. No new compiler recognition of `Math` is
involved. The tiny LLVM helpers are `alwaysinline nounwind`; no fast-math flags,
approximations, or assumptions excluding NaNs, infinities, or signed zero are
enabled. As specified by the [LLVM 22.1 intrinsic contract](https://releases.llvm.org/22.1.0/docs/LangRef.html#llvm-sqrt-intrinsic),
the operation does not set C `errno` or provide floating-point traps. It uses
LLVM's ordinary floating-point environment assumptions, rather than offering
dynamic rounding-mode or exception-control semantics.

Windows x64 object inspection at O0 and O2 confirms `sqrtss` / `sqrtsd`
instructions and no undefined `sqrt` or `sqrtf` library symbols. This is evidence
for that backend configuration, not a promise that every software-float target
can avoid support routines. Windows floating-point code retains the documented
`_fltused` ABI marker requirement.

The complete `Math.gf` is **not currently a standalone no-runtime source**:
even an application using only `Sqrt` must satisfy the checked integer methods'
exception/allocation declarations during compilation. Selecting actual
`Math.gf` and `Exception.gf` with `NoStdLib` currently fails for missing
`Memory::Allocate` before linking. The square-root tests use the real library
and its normal contracts; this API does not claim a whole-Math no-CRT proof.
