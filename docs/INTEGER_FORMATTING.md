# Integer formatting into caller buffers

`std/core/IntegerFormatting.gf` supplies `std::IntegerFormatting::TryFormat`
overloads for `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`,
`nint`, and `nuint`. The functions are `nothrow` and require no String, libc,
allocation, exception runtime, or writer interface.

```csharp
char[20] buffer;
nuint written = 0;
bool success = std::IntegerFormatting::TryFormat(-123, buffer, 20, ref written);
// success == true; the first written bytes are "-123".
```

Output is invariant ASCII decimal: no grouping, leading plus sign, padding, or
leading zeros. Negative values have a leading minus sign; zero is one byte.
Signed minimum values are supported without signed negation overflow. Pointer
integers use the target's pointer width.

The result is counted bytes with **no null terminator**. A 20-byte buffer is
sufficient for any supported integer. Callers needing a C string must reserve
another byte and append the terminator themselves after success.

Each call first counts decimal digits, then writes directly backwards into the
output after checking capacity. No temporary-buffer copy or libc helper is needed.
Insufficient capacity returns false, sets `written` to zero, and leaves every
output byte untouched. A zero capacity does not access output storage. On success
exactly `written` bytes change; bytes outside that range remain unchanged.
The caller supplies writable storage for the stated capacity and must keep
`written` storage disjoint from the output buffer.

The existing writer-based `std::Format` functions remain independently usable
with their existing source dependencies. This optional source is intended for
caller-buffer and freestanding code that does not need writer dispatch.

## Hexadecimal

`TryFormatHex` supports the ten decimal integer types plus the signed 128-bit
`extralong` type:

```csharp
char[16] address;
nuint written = 0;
std::IntegerFormatting::TryFormatHex(4660ul, address, 16, ref written, 16, false);
// The counted result is "0000000000001234".
```

The argument order is `(value, output, capacity, ref written,
minimumDigits = 1, uppercase = true)`. Results contain ASCII hexadecimal digits,
without `0x`, a minus sign, or a terminator. Zero always produces at least one
digit, including when `minimumDigits` is zero. Both cases use invariant digits.

`minimumDigits` is a padding width, not a limit: natural digits are never
truncated, and larger widths prepend zeros even beyond eight or sixteen digits.
For example, `-1` as an `int` produces `FFFFFFFF`; as a `long` it produces
`FFFFFFFFFFFFFFFF`. These are the original type's two's-complement bits; an
`int` is not sign-extended to 64 bits. Likewise, `sbyte` -1 produces `FF`,
`short` -1 produces `FFFF`, and `nint` uses the target pointer width.
An `extralong` negative value uses all 32 hexadecimal digits; positive values
omit leading zeros unless padding is requested. For example, `(extralong)-1`
produces 32 `F` digits. A 32-byte buffer holds any unpadded `extralong` result.
A width of 20 pads shorter results with leading zeros to reach 20 characters;
longer results are never truncated.

The whole result is preflighted before writing. Insufficient capacity sets
`written` to zero and leaves all output bytes unchanged, including for an
extremely large requested padding width. Required length is the greater of
natural digit count and padding width; it does not add values that could
overflow. The decimal buffer lifetime and count-output separation rules above
apply equally here. All hexadecimal overloads are `nothrow`, allocate nothing,
and add no source or runtime dependencies.

Decimal `TryFormat(extralong)` and writer-based `Format(extralong, writer)` are
not supplied yet; calls are rejected rather than narrowing to 64 bits. Hexadecimal
formatting uses shifts and masks, so adding this overload does not introduce
128-bit division helpers or a compiler-runtime dependency for standard builds.
