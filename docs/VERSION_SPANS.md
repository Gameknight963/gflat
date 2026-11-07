# Parse and format versions through spans

`Version.Parse` and `Version.TryParse` accept `Span<readonly(char)>`. Both
`TryFormat` forms accept `Span<char>`, including the overload selecting how
many version fields to write.

```csharp
using Standard;
using Standard::Memory;

int main()
{
    readonly(char)* text = "[1.2.3.4]";
    Version version = Version::Parse(new Span<readonly(char)>(text + 1, 7));
    char[3] output;
    nuint written = 0;
    if (!version.TryFormat(new Span<char>(&output[0], 3), ref written, 2)) return 1;
    if (written != 3 || output[0] != '1' || output[2] != '2') return 2;
    return 0;
}
```

Parsing consumes the complete span without requiring a terminator. Embedded
nulls are invalid. Invalid or empty input makes `TryParse` return false and
reset the output to `new Version()`. `Parse` throws `Exception` instead.
The existing strict dotted-number grammar is unchanged.

`TryFormat` writes counted bytes without a terminator. Short destinations and
unavailable field counts return false, zero `written`, and preserve the
destination. Selecting zero fields succeeds even with a default empty span.
The default format always needs space for at least two fields.

Valid parsing and all `Try` operations allocate nothing. The `Try` operations
are `nothrow`. Existing backed-overlap rules apply, and `written` must be
separate from the receiver and destination. See [versions](VERSION.md) and
[field-count formatting](VERSION_FORMATTING.md).

Selective source builds also include `Memory/Span.gf`, `Queries/Enumerable.gf`,
and `Collections/IEnumerable.gf` alongside the existing version dependencies.
