# Borrowed text prefix and suffix checks

`Text/text_match_span.gf` adds `StartsWith` and `EndsWith` to
`Span<readonly(char)>`, including the views returned by `Split`, `Lines`, and
`Trim`. Include `Memory/Span.gf`, `Text/CString.gf`, and their ordinary span dependencies
when selecting sources individually.

```csharp
using Standard::Text;
using Standard::Memory;
Span<readonly(char)> token = new("name=value!", 10); // Excludes the '!'.
bool named = token.StartsWith("name=");
bool suffix = token.EndsWith("value", 5);
```

Both methods accept a null-terminated pattern or a pointer plus byte count:

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

Only the source span's counted bytes are compared; it need not end with a null.
Comparisons are case-sensitive byte equality. The counted pattern can contain
embedded nulls or any byte value; the C-string form stops at its first null.
An empty pattern matches every span, including the default empty span. A pattern
longer than the span returns false. Counted zero-length and longer-pattern checks
read no pattern bytes. Pointer arguments still have non-null types.

Source and pattern may overlap. Neither changes, allocates, or retains a pointer.
The receiver is borrowed; calling the method does not copy the span or source
text or extend either buffer's lifetime. Keep both regions readable for the call.
Mutable spans can use `text.AsReadOnly().StartsWith(...)` or `EndsWith(...)`.
These extensions use the existing checked Span indexer and do not add a
`nothrow` guarantee to it.

For splitting and ASCII trimming, see [borrowed text parts](TEXT_PARTS.md).
