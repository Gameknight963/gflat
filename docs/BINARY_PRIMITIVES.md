# Binary scalar buffers

`Standard::Binary::BinaryPrimitives` reads and writes scalars in little- or
big-endian byte order. Buffer addresses may be unaligned.

| Name in method | Value type | Bytes |
| --- | --- | --- |
| `Int16`, `UInt16` | `short`, `ushort` | 2 |
| `Int32`, `UInt32` | `int`, `uint` | 4 |
| `Int64`, `UInt64` | `long`, `ulong` | 8 |
| `ExtraLong` | `extralong` | 16 |
| `Float`, `Double` | `float`, `double` | 4, 8 |

Method names combine `TryRead` or `TryWrite`, the scalar name, and
`LittleEndian` or `BigEndian`:

```csharp
using Standard;
using Standard::Binary;

byte[8] data;
bool written = BinaryPrimitives::TryWriteUInt32BigEndian(data, 8, 305419896u);
uint value = 0u;
bool read = BinaryPrimitives::TryReadUInt32BigEndian(data, 8, ref value);
```

The buffer count must cover the scalar's width. A short read returns false and
sets the result to zero. A short write returns false and preserves the buffer.
Successful operations use exactly the scalar's width.

Read outputs may overlap input. The method loads all source bytes before storing
the result. On a short read, writing the zero result still changes overlapping
input bytes. Scalar output variables require their normal alignment.

## Signed 128-bit values

The `ExtraLong` methods preserve all sixteen bytes of the two's-complement
representation, including the minimum signed value. Matching methods are also
available on [binary cursors](BINARY_CURSORS.md) and
[binary streams](BINARY_IO.md).

## Floating-point values

The `Float` and `Double` methods preserve exact IEEE 754 bits, including negative
zero and NaN signs and payloads. A short read produces positive zero.

On Windows x64, including the floating-point definitions can require `_fltused`
even when a caller only uses integer methods at O0. The CRT normally supplies
this marker. Freestanding programs provide it in their startup code. See
[Windows floating-point startup](FREESTANDING_CORE.md#windows-floating-point-marker).

For stream operations and their EOF/error behavior, see [binary IO](BINARY_IO.md).

Source: `core/Binary/BinaryPrimitives.gf`
