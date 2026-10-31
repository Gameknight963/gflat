# Borrowed UTF-16 scalar iteration

`Standard::Text::Utf16Scalars` provides `foreach` over `ushort` code units:

```csharp
using Standard::Text;

ushort[4] units = [(ushort)65, (ushort)0, (ushort)0xd83d, (ushort)0xde00];
Utf16Scalars scalars = new(units, 4);
foreach (uint scalar in scalars) {
    // U+0041, U+0000, U+1F600
}
```

`CodeUnitLength` reports the captured unit count. A null pointer is valid for
empty input. Each `GetEnumerator()` starts at the beginning and caches decoded
`uint` values in `Current`. `AsEnumerable()` enables
[queries](../Standard/core/Queries/README.md).

Keep backing units alive while using the view, iterator, or query. Copies have
independent positions over the same storage. Changes to unread units affect
later decoding, while an already cached `Current` stays unchanged.

Malformed surrogate sequences throw when `MoveNext()` reaches them and invalidate
`Current`. The position remains at the invalid sequence, so retrying unchanged
input throws again. Breaking early leaves the remaining input unvalidated.
Use `Utf16::IsValid` for full validation or `Utf16Decoder` for chunked input.

Source: `core/Text/Utf16Scalars.gf`
