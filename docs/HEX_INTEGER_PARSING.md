# Hexadecimal integer parsing

`Standard::Text` provides unsigned hexadecimal parsers for `Byte`, `UShort`,
`UInt`, `ULong`, and `NUInt`:

```cpp
using Standard::Text;

bool TryParseHexULong(readonly(char)* text, ulong* result) nothrow;
bool TryParseHexULong(readonly(char)* text, nuint count, ulong* result) nothrow;
ulong ParseHexULong(readonly(char)* text);
ulong ParseHexULong(readonly(char)* text, nuint count);
```

Digits are `0`–`9`, `a`–`f`, and `A`–`F`. A leading `0x` or `0X` is optional.
At least one digit is required. Signs, whitespace, separators, trailing text,
and out-of-range values fail. `NUInt` uses the target's unsigned pointer range.

```cpp
using Standard;
using Standard::Text;

uint flags = ParseHexUInt("0x80000001");
ulong address = 0ul;
bool valid = TryParseHexULong("fF0012", &address);
```

`TryParseHex...` returns false and writes zero on failure. `ParseHex...` throws
`Exception` for the same inputs.

C-string forms stop at NUL. Counted forms validate every selected byte, so
embedded NUL is invalid. Parsing `"0xFF"` with count one selects the valid digit
`"0"`. Count two selects an incomplete prefix and fails.

Output may overlap aligned writable input. Reading finishes before the result
is stored, including the zero result on failure. For String input, pass
`text.Data, text.Length`.

Sources: `core/Text/hex_integer_parsing.gf` for Try forms and
`core/Text/hex_integer_parsing_throwing.gf` for throwing forms.
