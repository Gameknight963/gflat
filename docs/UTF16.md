# Strict UTF-16 code-unit helpers

`std/core/Text/utf16.gf` supplies `std::Text::Utf16`, an allocation-free, `nothrow` core
codec with no String, libc, GC, or exception-runtime dependency.

Inputs and outputs are **host `ushort` code-unit values**. Lengths, capacities,
consumed counts, and written counts all count code units, not bytes. The codec
does not parse an endian-specific byte stream, swap byte order, remove a BOM,
append a terminator, normalize text, or count grapheme clusters. U+FEFF is an
ordinary scalar, and embedded U+0000 participates in counted input.

| Function | Result |
| --- | --- |
| `IsValidScalar(uint scalar)` | Excludes surrogate values and values above U+10FFFF. |
| `EncodedLength(uint scalar)` | 1 or 2 code units; zero for invalid scalars. |
| `TryDecode(input, count, scalar, consumed)` | Decodes the first scalar; remaining input is not validated. |
| `TryDecodeLast(input, count, scalar, consumed)` | Decodes the final scalar; preceding input is not validated. |
| `TryEncode(scalar, output, capacity, written)` | Writes one scalar without a terminator. |
| `TryCount(input, count, scalarCount)` | Validates all input and counts Unicode scalars. |
| `IsValid(input, count)` | Validates all input. |

The input pointer is `readonly(ushort)*`, encoded output is `ushort*`, scalar
output is `uint*`, and count outputs are `nuint*`. The caller supplies valid,
appropriately aligned storage. A zero input count performs no reads through the
input pointer; empty input is valid and contains zero scalars. Decoding an empty
input returns false. Output metadata pointers must always be writable.

High surrogates U+D800-U+DBFF must be followed by a low surrogate U+DC00-U+DFFF.
Isolated low surrogates, truncated pairs, and incorrectly paired surrogates fail.
No replacement character is emitted. Noncharacters such as U+FFFF are valid
Unicode scalar values and are accepted.

On decode failure, scalar and consumed outputs are both zero. These two outputs
must be disjoint from each other, but either may overlap input storage: stores
occur only after all units required to decide the result have been read.
`TryCount` likewise permits its count output to overlap input, storing only after
the required scan; validation failure sets it to zero.

Encoding invalid scalars or with insufficient capacity returns false, sets
written to zero, and leaves encoded output untouched. The written-count storage
must be disjoint from encoded output. A successful supplementary scalar writes
its complete surrogate pair; failures never leave a partially encoded pair.

`TryDecodeLast` accesses only the final one or two code units, returning the
final scalar and its length. An isolated final low surrogate, a wrong preceding
surrogate, or a truncated final high surrogate fails with both outputs zero.
The preceding prefix is not validated: a valid final BMP scalar or surrogate
pair after malformed units can succeed. Its output alignment and overlap rules
are the same as `TryDecode`; result stores may overwrite the retained prefix.

To shorten a counted view by one scalar, decode its end and subtract the
consumed code units. Repeating this operation walks backward without an index
or transcoding. This follows scalar boundaries, not grapheme clusters.

```csharp
int main()
{
    ushort[3] text = [(ushort)65, (ushort)0xd83d, (ushort)0xde00];
    nuint length = 3;
    uint scalar = 0u;
    nuint consumed = 0;
    if (!std::Text::Utf16::TryDecodeLast(text, length, &scalar, &consumed)) return 1;
    if (scalar != 0x1f600u || consumed != (nuint)2) return 2;
    length -= consumed;
    if (length != (nuint)1 || text[0] != (ushort)65) return 3;
    return 0;
}
```

## Incremental decoding

`std/core/Text/Utf16Decoder.gf` independently supplies the value type
`std::Text::Utf16Decoder`, mirroring `Utf8Decoder` with `ushort` input. Its constructor,
`Reset`, and `Read` are `nothrow`; no other library source is required.

`Read(input, count, final, scalar, consumed)` produces at most one scalar.
`consumed` counts units examined from this call's chunk, including an offending
unit on failure. No input address is retained: a high surrogate at a chunk's end
is saved as a value and completed from a later chunk. Non-scalar statuses clear
the scalar output to zero. Metadata outputs must be disjoint from each other,
the current input, and decoder state.

| Status | Meaning |
| --- | --- |
| `Scalar` | One scalar produced, including possibly U+0000. Call again on any remaining units. |
| `NeedMore` | Current non-final input exhausted. Supply another chunk, possibly empty. |
| `End` | Final input exhausted with no incomplete pair. |
| `Invalid` | An isolated/incorrect surrogate or an incomplete final pair was observed. |

`final` means no input follows the supplied chunk. A `Scalar` result still takes
priority: process it and call again with the remaining chunk, or zero units at
its end, to receive `End` or discover a malformed suffix. Do not treat a final
`Scalar` as implicit successful completion of all input.

`End` and `Invalid` persist until `Reset`; subsequent calls consume zero units.
Empty non-final calls preserve partial state and return `NeedMore`. An empty
final call reports `Invalid` for a pending high surrogate and `End` otherwise.
Copying the decoder copies its complete state; copies progress independently.
