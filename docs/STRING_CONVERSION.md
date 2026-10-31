# Convert a value to an owning String

Import `Standard::Text` to use `ToString()`:

```csharp
using Standard::Text;
using Standard;
using Standard::Time;

int main()
{
    DateTime moment = DateTime::Parse("2026-10-24T12:34:56");
    String dateText = moment.ToString();
    int count = 42;
    String countText = count.ToString();
    if (!dateText.Equals("2026-10-24T12:34:56") || !countText.Equals("42")) return 1;
    return 0;
}
```

For `IFormattable` values, the extension calls `Format` with a new String as the
writer. A type's instance `ToString` takes priority. Formatter and allocation
errors propagate, and partial result storage is cleaned up.

| Value | Representation |
| --- | --- |
| Integers, including `extralong` | Decimal |
| `bool` | `true` or `false` |
| `char` | One counted byte, including NUL |
| `float`, `double` | Current C locale, 9/17 significant digits |
| `IFormattable` | Its `Format` output |

The returned String owns its text. Keep it alive while using a pointer from
`Data` or `ToCString()`. To copy a C string into a String, use `String::From(text)`.

Sources: `core/Text/formattable_string.gf`, `primitive_string.gf`,
`extra_long_string.gf`, and `libc/Text/floating_string.gf`.
