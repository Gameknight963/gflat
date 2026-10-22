# Hexadecimal integer parsing

`core/HexIntegerParsing.gf` provides unsigned hexadecimal parsing in `std` for
`Byte`, `UShort`, `UInt`, `ULong`, and `NUInt`. Names follow the decimal parsers:

```cpp
bool TryParseHexULong(readonly(char)* text, ulong* result) nothrow;
bool TryParseHexULong(readonly(char)* text, nuint count, ulong* result) nothrow;
ulong ParseHexULong(readonly(char)* text);
ulong ParseHexULong(readonly(char)* text, nuint count);
```

Digits are `0`–`9`, `a`–`f`, and `A`–`F`. A leading `0x` or `0X` is optional,
but at least one digit is required. Leading zeroes are allowed. Signs, whitespace,
underscores, trailing characters, and values outside the destination's range are
rejected. Signed and two's-complement interpretations are not provided.
`NUInt` uses the target pointer-sized unsigned limit.

```cpp
using std;
uint flags = ParseHexUInt("0x80000001");
ulong address = 0ul;
bool valid = TryParseHexULong("fF0012", &address);
```

The C-string form stops at the first zero byte. The counted form consumes exactly
`count` bytes and needs no terminator; zero bytes within that range are invalid.
For example, parsing `"0xFF"` with count `1` reads the complete digit sequence
`"0"`, whereas count `2` selects an incomplete prefix and fails. A `String` can
be passed using `text.Data, text.Length` to validate all of its counted bytes.

`TryParseHex...` returns false and writes zero on every failure. It is `nothrow`
and allocates nothing. `ParseHex...` throws `Exception` on the same invalid or
out-of-range input. Neither form modifies disjoint input storage. The input must
cover the requested readable bytes, and the result pointer must designate
writable storage. Successful parsing finishes reading before committing its
output, so suitably aligned output may overlap the input. On failure, the zero
output also changes any overlapping input bytes.

Overflow is checked before multiplication and addition, including at the
unsigned 64-bit maximum. The implementation depends on no libc functions and is
independent of the decimal parsing source files. Including the complete file
still requires the normal exception definitions and runtime for its throwing
forms.
