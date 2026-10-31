# Variable-length integers

`Standard::Binary::VarInt` encodes unsigned integers in base-128. Each byte
contributes seven value bits, least-significant group first. Bit 7 indicates
another byte follows.

| Value | Encoded bytes |
| --- | --- |
| 0 | `00` |
| 127 | `7F` |
| 128 | `80 01` |
| 300 | `AC 02` |

UInt32 uses at most five bytes. UInt64 uses at most ten. The shortest
representation is required.

## Signed values with ZigZag

`ZigZagEncode` maps `int` to `uint` and `long` to `ulong`.
`ZigZagDecode` reverses the mapping across the full range:

| Signed value | Unsigned code |
| --- | --- |
| 0, -1, 1, -2, 2 | 0, 1, 2, 3, 4 |
| Signed minimum | Unsigned maximum |
| Signed maximum | Unsigned maximum minus one |

```csharp
using Standard;
using Standard::Binary;

int main()
{
    int original = -150;
    uint encoded = VarInt::ZigZagEncode(original);
    byte[5] buffer;
    nuint written = 0;
    if (!VarInt::TryWriteUInt32(buffer, 5, encoded, ref written)) return 1;
    uint decoded = 0u;
    nuint consumed = 0;
    if (VarInt::ReadUInt32(buffer, written, ref decoded, ref consumed) != VarInt::Status::Done)
        return 2;
    int restored = VarInt::ZigZagDecode(decoded);
    if (restored != original || consumed != written) return 3;
    return 0;
}
```

This corresponds to Protocol Buffers `sint32` and `sint64`. Its `int32` and
`int64` fields encode negative numbers differently. See the
[protobuf encoding reference](https://protobuf.dev/programming-guides/encoding/#signed-ints).

## Writing

`GetUInt32Length(value)` and `GetUInt64Length(value)` return the encoded size.
`TryWriteUInt32(output, capacity, value, ref written)` and the UInt64 form write
the encoding. Short capacity returns false, sets `written` to zero, and preserves
the buffer.

## Reading

`ReadUInt32(data, count, ref value, ref consumed)` and the UInt64 form return:

| Status | Meaning |
| --- | --- |
| `Done` | Decoded one value. `consumed` identifies the next field. |
| `NeedMore` | A valid unfinished prefix, including empty input |
| `Invalid` | Overflow, continuation beyond the width, or an overlong encoding |

Both failure statuses preserve `value` and set `consumed` to zero. Retry
`NeedMore` with the same prefix and additional bytes. If input is final, that
status means truncation. Encodings such as `80 00` and `81 00` are invalid even
though some protobuf readers accept them.

Keep scalar output references separate from the byte buffers and each other.

```csharp
using Standard;
using Standard::Binary;

int main()
{
    byte[10] data;
    nuint written = 0;
    if (!VarInt::TryWriteUInt64(data, 10, 300ul, ref written)) return 1;
    ulong value = 0ul;
    nuint consumed = 0;
    if (VarInt::ReadUInt64(data, written, ref value, ref consumed) != VarInt::Status::Done)
        return 2;
    if (value != 300ul || consumed != written) return 3;
    return 0;
}
```

For cursors, use [binary buffer varints](BINARY_CURSORS.md#optional-variable-length-integers).
For IO, use [stream extensions](STREAM_VARINT.md) or [FileVarInt](FILE_VARINT.md).

Source: `core/Binary/VarInt.gf`
