# Boolean parsing

`Standard::Text::TryParseBool` accepts ASCII `true` and `false`, ignoring letter case.
The entire token must match. For example, `"TRUE"` succeeds while `" true "` and
`"1"` fail.

```cpp
using Standard::Text;

bool TryParseBool(readonly(char)* text, bool* result) nothrow;
bool TryParseBool(readonly(char)* text, nuint count, bool* result) nothrow;
```

Success returns true and stores the parsed value. Failure returns false and
stores false:

```cpp
using Standard;
using Standard::Text;

bool enabled = true;
bool valid = TryParseBool("FaLsE", &enabled); // valid=true, enabled=false
```

The C-string overload stops at its first NUL. The counted overload validates
every selected byte, so `"true\0suffix"` fails when the suffix is counted.
Empty input fails.

`ParseBool` has the same counted and C-string forms and throws `Exception`
for invalid text. For a String, pass `text.Data, text.Length`.

Output may overlap aligned writable input storage. The parser finishes reading
before storing its result, including the false result on failure.

## Borrowed spans

`Span<readonly(char)>` supports `text.TryParseBool(&result)` and
`text.ParseBool()`. Both use the span's complete byte count and accept an
immutable span header. Empty spans fail.

Sources in `core/Text`:

- `boolean_parsing.gf`: pointer-based Try forms
- `boolean_parsing_throwing.gf`: throwing forms
- `span_boolean_parsing.gf` and `span_boolean_parsing_throwing.gf`: span forms
