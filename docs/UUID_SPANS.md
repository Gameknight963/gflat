# UUID text and byte spans

`Uuid` accepts spans in `Parse`, `TryParse`, `ParseExact`, `TryParseExact`,
`FromBytes`, and `TryFromBytes`. `TryFormat` and `TryWriteBytes` accept writable
destination spans.

```csharp
using Standard;
using Standard::Memory;

int main()
{
    readonly(char)* text = "[00112233-4455-6677-8899-aabbccddeeff]";
    Uuid id = Uuid::Parse(new Span<readonly(char)>(text + 1, 36));
    byte[16] bytes;
    nuint written = 0;
    if (!id.TryWriteBytes(new Span<byte>(&bytes[0], 16), ref written)) return 1;
    Uuid copy = Uuid::FromBytes(new Span<readonly(byte)>(&bytes[0], written));
    if (!id.Equals(&copy)) return 2;
    return 0;
}
```

Text spans contain the complete input, with no terminator required. Embedded
nulls are invalid. `Parse` uses the canonical hyphenated spelling. The exact
forms accept the existing `N`, `D`, `B`, and `P` format characters, in either
case. See [UUID formats](UUID_FORMATS.md).

Byte input must contain exactly 16 bytes in network order, with the most
significant byte first. This matches `Guid` byte conversion with
`bigEndian: true`. See [canonical UUID bytes](UUID.md#canonical-bytes).

Invalid input makes the `Try` parsing/construction forms return false and reset
the output to nil. The corresponding throwing forms raise `Exception` instead.
Successful parsing and construction allocate nothing. The `Try` methods are
`nothrow` and allocate nothing, including on invalid input.

Short destination spans return false with `written` set to zero and leave the
bytes unchanged. Unsupported text format characters also return false.
Successful text output has no terminator. `written` must be separate from the
destination and receiver. The existing byte API's backed-overlap support still
applies to byte spans.

Selective builds also include `Memory/Span.gf`, `Queries/Enumerable.gf`, and
`Collections/IEnumerable.gf`, alongside the existing UUID dependencies.
