# Scalar math

`std/core/Math.gf` provides `std::Math` without libc or native math libraries.

## Integers

`Min(a, b)`, `Max(a, b)`, and `Clamp(value, minimum, maximum)` have ordinary
overloads for `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`,
`nint`, and `nuint`. Each returns the same integer type as its arguments.
`Clamp` includes both bounds and throws `Exception` if minimum exceeds maximum.
Use matching argument types; these APIs do not define mixed signed/unsigned
comparison rules of their own.

`Abs(value)` has overloads for the signed types. The most negative value has no
positive counterpart in the same type, so it throws `Exception` rather than
wrapping or invoking signed overflow. This check also applies to pointer-sized
`nint` values.

## Floating point

`float` and `double` each have `IsNaN`, `IsFinite`, `Abs`, `Min`, and `Max`
overloads. `IsFinite` excludes both infinities and all NaNs, and includes zeros
and subnormals. `Abs` clears the sign bit, including for negative zero and NaNs.

`Min` and `Max` propagate a NaN input rather than silently selecting the numeric
input. When both inputs are NaN the first is returned. When comparing positive
and negative zero, `Min` chooses negative zero and `Max` chooses positive zero,
independently of argument order. Equal zeros of the same sign retain that sign.
Other finite and infinite values use ordinary comparisons.

Floating-point values use the language's IEEE binary32/binary64 representation;
these helpers inspect the existing representation using pointer casts and need
no compiler intrinsics or runtime allocation on their normal paths.
