# Hosted floating-point parsing

`Standard::Text` provides C-string conversions through libc:

```csharp
bool TryParseFloat(readonly(char)* text, float* result) nothrow;
bool TryParseDouble(readonly(char)* text, double* result) nothrow;
```

Success writes the converted value. Failure returns false and writes positive
zero. Surrounding whitespace or an unconsumed suffix fails.

```csharp
using Standard::Text;

int main()
{
    float single = 0.0f;
    double wide = 0.0;
    if (!TryParseFloat("1.25", &single) || single != 1.25f) return 1;
    if (!TryParseDouble("-0", &wide) || wide.reinterpret<ulong>() != 0x8000000000000000ul) return 2;
    if (TryParseDouble("1.25 trailing", &wide) || wide.reinterpret<ulong>() != 0ul) return 3;
    if (!TryParseDouble("1e9999", &wide) || wide.reinterpret<ulong>() != 0x7ff0000000000000ul) return 4;
    return 0;
}
```

## Grammar, locale and range

Float uses strtof and double uses strtod. Accepted syntax and radix character
follow the current C locale. In the initial locale, examples include `+1.25`,
`.5`, `1e-3`, `0x1.8p2`, `INF`, `-infinity`, and `nan`.

Overflow can return infinity, and underflow can return a subnormal or signed
zero. These are successful conversions. NaN syntax and payload representation
follow the host library.

Output may overlap aligned writable input storage. It is stored after input
reading and end validation.

## Optional throwing wrappers

```csharp
float ParseFloat(readonly(char)* text);
double ParseDouble(readonly(char)* text);
```

ParseFloat and ParseDouble throw Exception for invalid syntax or trailing input.
Range results remain successful, so `ParseDouble("1e9999")` can return infinity.

## C strings are not counted tokens

For a counted token, reject embedded NUL if all bytes must participate, then
provide a terminated copy. Passing String.Data parses its C-string prefix.

Sources: `libc/Text/floating_parsing.gf`, `floating_parsing_throwing.gf`.
See [integer parsing](INTEGER_PARSING.md) and [String conversion](STRING_CONVERSION.md).
