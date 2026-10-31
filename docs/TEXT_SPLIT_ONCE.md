# Split a borrowed key and value

`Span<readonly(char)>.TrySplitOnce` separates text at its first matching byte:

```csharp
using Standard::Memory;

extension Span<readonly(char)> {
    public readonly bool TrySplitOnce(char separator, ref Span<readonly(char)> before, ref Span<readonly(char)> after);
}
```

```csharp
using Standard::Text;
using Standard;
using Standard::Memory;

String setting = s"mode=fast=checked";
Span<readonly(char)> input = setting.AsReadOnly();
Span<readonly(char)> key = new();
Span<readonly(char)> value = new();
bool found = input.TrySplitOnce('=', ref key, ref value);
// found is true; key is "mode" and value is "fast=checked".
```

The separator is excluded. Later separators stay in `after`. A separator at an
endpoint produces an empty side. Absence returns false and resets both outputs
to empty spans with null `Data`.

Use two distinct output variables. Either can reuse the receiver variable:
`input.TrySplitOnce('=', ref key, ref input)` keeps the remainder in `input`.
Keep the original owner alive while using the returned spans.

`TrySplitLastOnce` uses the last match. Splitting `"a=b=c"` at `=` gives `"a=b"`
and `"c"`. [Byte-sequence overloads](TEXT_SPLIT_PATTERN.md) handle longer
separators. For every field, use [Split](TEXT_PARTS.md).

Sources: `core/Text/text_split_once_span.gf`, `text_split_last_once_span.gf`
