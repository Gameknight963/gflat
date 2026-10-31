# Hosted floating-point parsing

`Standard/libc/Text/floating_parsing.gf` provides C-string conversions in
`Standard::Text`:

```csharp
bool TryParseFloat(readonly(char)* text, float* result) nothrow;
bool TryParseDouble(readonly(char)* text, double* result) nothrow;
```

They require nonempty, fully consumed input with no surrounding whitespace.
Failure returns false and writes positive zero. A valid zero returns true.
Input must be readable through its terminating NUL, and result must point to
writable, correctly aligned storage. Output is committed after all input reads
and end validation, so valid input/output overlap is supported.

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

These are hosted libc conversions, using `strtof` for float and `strtod` for double.
They use the **current C locale**, as existing floating formatting does. The native
grammar accepts decimal/scientific values, hexadecimal floats, infinity and NaN
forms. NaN payload syntax and representation follow the host library. Leading
whitespace is rejected using libc's unsigned-byte `isspace` classification, and
any unconsumed suffix, including trailing whitespace, fails.

In the initial C locale, examples of valid inputs include `+1.25`, `.5`, `1.`,
`1e-3`, `0x1.8p2`, `INF`, `-infinity` and `nan`. Empty input, a sign alone,
`1e+`, `1x`, and ` 1` fail. The radix character and additional accepted forms
can change with the current locale. This is not an invariant decimal parser.
Changing the CRT locale externally can change behavior. Text formatted under one
locale is not promised to parse under another.

Successful parsing preserves the native converted value. Overflow can produce
infinity. Underflow can produce a subnormal or signed zero. These are successful
conversions, not syntax failures. No overflow/underflow failure guarantee, errno
management or NaN-payload preservation is provided. Float calls `strtof` directly
to avoid introducing a double-to-float rounding step in the wrapper.

The wrappers make no temporary text copy or gflat allocation and do not need
String, Math, memory hooks or exception declarations. They still depend on libc.
This is not a promise that libc itself never allocates internally. Native ABI
support such as Windows `_fltused` remains separate from wrapper allocations.

## Optional throwing wrappers

`Standard/libc/Text/floating_parsing_throwing.gf` adds:

```csharp
float ParseFloat(readonly(char)* text);
double ParseDouble(readonly(char)* text);
```

They throw Exception for the same syntax/full-input failures that make TryParse
return false. Native range values remain accepted: `ParseDouble("1e9999")` can
return infinity. These wrappers require the Try source and normal Exception,
allocation and exception-runtime support. This differs intentionally from integer
parsing, where overflow cannot produce a representable result and is rejected.

## C strings are not counted tokens

There is no counted, Span or String overload. Native conversion reads to the
terminator. Checking endptr afterward cannot make an unterminated counted span
safe. Passing String.Data intentionally parses only its C-string prefix: for
example `"1.5\0suffix"` parses as `1.5`. To validate every byte of a counted token,
reject embedded NULs and explicitly provide terminated storage. A terminated copy
alone does not reject a suffix after an embedded NUL.

Use the [integer parsers](INTEGER_PARSING.md) for strict counted decimal integers.
Floating formatting and owning conversions retain their existing libc and locale
policy. See [String conversion](STRING_CONVERSION.md).
