# ASCII byte utilities

`core/Ascii.gf` provides `std::Ascii` functions for the language's byte-sized
`char`. These functions do not interpret individual UTF-8 bytes as Unicode
characters. They work without std, libc, allocation, or exception support.

Classification functions are `IsAscii`, `IsDigit`, `IsLower`, `IsUpper`,
`IsLetter`, `IsLetterOrDigit`, `IsHexDigit`, `IsWhiteSpace`, `IsControl`, and
`IsPrintable`. Every function accepts a `char` and returns `bool`.

- ASCII comprises bytes 0 through 127.
- Letters are A–Z and a–z; decimal digits are 0–9.
- Hex digits additionally include A–F and a–f.
- Whitespace is space, tab, line feed, vertical tab, form feed, and carriage return.
- Control bytes are 0–31 and 127.
- Printable bytes are 32–126, including space.

All classification functions return false for bytes 128–255.
`ToLower(char)` and `ToUpper(char)` convert ASCII letters and preserve every
other byte, including bytes above 127. Results do not depend on locale or the
machine's signed `char` conventions.

`TryHexValue(char input, byte* result)` recognizes one hexadecimal digit and
writes its numerical value from 0 through 15. Invalid input returns false and
writes zero. Use `std::Utf8` to decode Unicode scalar values before applying
any future Unicode character operations.

## Counted case-insensitive comparison

These counted operations are also `nothrow` and allocation-free:

```csharp
bool command = std::Ascii::EqualsIgnoreCase(input, inputLength, "help", 4);
int order = std::Ascii::CompareIgnoreCase(left, leftLength, right, rightLength);
```

Lengths count bytes, including embedded nulls. Buffers are borrowed and are never
modified. Each pointer must designate at least its supplied length of readable
storage; a zero length performs no reads through that pointer.

`CompareIgnoreCase` returns exactly -1, 0, or 1. It folds ASCII `a`-`z` to
uppercase, then compares unsigned bytes lexicographically. If the common prefix
is equal, the shorter input sorts first. Uppercase folding also determines
punctuation ordering: `z` sorts before `[`. For ASCII inputs, this agrees with
.NET's `StringComparison.OrdinalIgnoreCase` ordering.

Bytes 128-255 remain unchanged. This is not Unicode case folding, UTF-8 decoding,
or a locale-sensitive comparison. For example, differently cased accented letters
encoded in UTF-8 do not become equal. `EqualsIgnoreCase` returns false immediately
when lengths differ and otherwise applies the same ASCII-only rules.

## Null-terminated comparison

Two-argument overloads accept C strings:

```csharp
bool command = std::Ascii::EqualsIgnoreCase(input, "help");
int order = std::Ascii::CompareIgnoreCase(left, right);
```

Both stop at the first null byte and use the same unsigned ASCII-only folding
and -1/0/1 ordering as the counted forms. Bytes after a terminator do not affect
the result. Both pointers must designate readable null-terminated strings.
These overloads are also `nothrow`, allocate nothing, and do not modify input.
Use the counted forms to compare embedded null bytes as part of the text.
