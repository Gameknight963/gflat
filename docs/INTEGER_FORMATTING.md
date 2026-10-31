# Integer formatting into caller buffers

`Standard::Text::IntegerFormatting::TryFormat` writes decimal ASCII for
`sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `nint`,
`nuint`, and `extralong`.

```csharp
char[20] buffer;
nuint written = 0;
bool success = Standard::Text::IntegerFormatting::TryFormat(-123, buffer, 20, ref written);
// success == true; the first written bytes are "-123".
```

The representation has a leading minus for negative values and otherwise uses
digits only. Zero is `0`. Signed minima are supported. `nint` and `nuint` use
the target pointer width.

The result is counted text without a NUL terminator. Reserve 20 bytes for any
integer through 64 bits or 40 for `extralong`. A C-string caller must append its
own terminator. Insufficient capacity returns false, sets `written` to zero,
and preserves the buffer. Keep `written` separate from output bytes.

## Hexadecimal

`TryFormatHex` accepts the same types:

```csharp
char[16] address;
nuint written = 0;
Standard::Text::IntegerFormatting::TryFormatHex(4660ul, address, 16, ref written, 16, false);
// The counted result is "0000000000001234".
```

Arguments are `(value, output, capacity, ref written, minimumDigits = 1,
uppercase = true)`. Output consists of hexadecimal digits, with leading zeroes
to satisfy `minimumDigits`. The natural representation is retained when it is
longer than the requested width. A zero value always emits at least one digit.

Signed values use their original type's two's-complement bits:

| Input | Unpadded uppercase result |
| --- | --- |
| `(sbyte)-1` | `FF` |
| `(int)-1` | `FFFFFFFF` |
| `(long)-1` | `FFFFFFFFFFFFFFFF` |
| `(extralong)-1` | 32 `F` digits |

Hexadecimal output omits `0x`, signs, and terminators. Capacity failure has the
same result as decimal formatting, including when the requested padding is huge.

Source: `core/Text/IntegerFormatting.gf`. Writer-based formatting is described in
[String conversion](STRING_CONVERSION.md).
