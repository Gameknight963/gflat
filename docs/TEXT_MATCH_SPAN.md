# Borrowed text prefix and suffix checks

`StartsWith` and `EndsWith` compare character spans against byte patterns:

```csharp
using Standard::Text;
using Standard::Memory;

Span<readonly(char)> token = new("name=value!", 10); // Excludes the '!'.
bool named = token.StartsWith("name=");
bool suffix = token.EndsWith("value", 5);
```

Each accepts a C string or counted bytes:

```csharp
using Standard::Memory;

extension Span<readonly(char)> {
    public readonly bool StartsWith(readonly(char)* prefix);
}
extension Span<readonly(char)> {
    public readonly bool StartsWith(readonly(char)* prefix, nuint count);
}
extension Span<readonly(char)> {
    public readonly bool EndsWith(readonly(char)* suffix);
}
extension Span<readonly(char)> {
    public readonly bool EndsWith(readonly(char)* suffix, nuint count);
}
```

Matching is case-sensitive byte equality. Counted patterns can contain NUL.
An empty pattern matches every span, while a pattern longer than the span fails.
Source and pattern may overlap.

For a mutable span, call `text.AsReadOnly()` first. For splitting and ASCII
trimming, see [borrowed text parts](TEXT_PARTS.md).

Source: `core/Text/text_match_span.gf`
