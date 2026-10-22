# Scalar math

`std/core/Math.gf` provides `std::Math`, with scalar helpers and checked integer
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

## Floating point

`float` and `double` each have `Sqrt`, `CopySign`, `IsNaN`, `IsFinite`, `Abs`, `Min`, and `Max`
overloads. `IsFinite` excludes both infinities and all NaNs, and includes zeros
and subnormals. `Abs` clears the sign bit, including for negative zero and NaNs.
All floating-point helpers are `nothrow`; the signed-integer `Abs` overloads
retain their overflow exceptions.

`CopySign(magnitude, sign)` returns the first argument with its sign bit replaced
by the second argument's sign bit. It preserves every other bit, including NaN
payloads and signaling bits. Negative zero and negative NaNs supply a negative
sign even though comparing them with zero would not detect it. Both overloads
use ordinary integer bit operations and allocate nothing.

`Min` and `Max` propagate a NaN input rather than silently selecting the numeric
input. When both inputs are NaN the first is returned. When comparing positive
and negative zero, `Min` chooses negative zero and `Max` chooses positive zero,
independently of argument order. Equal zeros of the same sign retain that sign.
Other finite and infinite values use ordinary comparisons.

Classification, `Abs`, `Min`, and `Max` inspect the language's IEEE
binary32/binary64 values using ordinary gflat operations and pointer casts.
They allocate nothing on their normal paths.

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
