# Variable-length unsigned integers

`core/VarInt.gf` provides `std::VarInt`, a standalone, allocation-free codec
for unsigned base-128 integers. All methods are `nothrow`; no other std source
is required.

Each byte contributes seven value bits, least-significant group first. Bit 7
means another byte follows. For example, 0 is `00`, 127 is `7F`, 128 is
`80 01`, and 300 is `AC 02`. UInt32 uses at most five bytes; UInt64 uses at
most ten. This is the unsigned integer encoding used by Protocol Buffers;
it does not implement signed `int32`/`int64` or ZigZag `sint32`/`sint64`.

## Writing

- `GetUInt32Length(uint value)` and `GetUInt64Length(ulong value)` return the
  exact encoded byte count, including one byte for zero.
- `TryWriteUInt32(byte* output, nuint capacity, uint value, ref nuint written)`
  and the UInt64 counterpart emit the shortest encoding.
- Insufficient capacity returns false, sets `written` to zero, and leaves all
  destination bytes unchanged. Success sets it to the exact length; bytes
  beyond that length are unchanged. No terminator is written.

## Reading

`ReadUInt32(readonly(byte)* data, nuint count, ref uint value, ref nuint consumed)`
and the UInt64 counterpart return `VarInt::Status`:

- `Done`: one complete canonical value was read. `value` holds the result and
  `consumed` holds its encoded length. Any following bytes are ignored.
- `NeedMore`: the buffer ends at a valid unfinished prefix, including empty
  input. Supply that same prefix together with more bytes on the next call;
  the decoder retains no state. A final `NeedMore` means truncated input.
- `Invalid`: the encoding overflows the destination width, still has a
  continuation bit in its last permitted byte, or uses more bytes than needed.

Both failure statuses leave `value` unchanged and set `consumed` to zero.
In particular, `80 00` and `81 00` are rejected even though some protobuf
readers accept these overlong encodings. This strictness makes the shortest
representation unique; do not assume it accepts every permissive protobuf
producer's output.

A zero count/capacity does not dereference the buffer. Otherwise, callers must
provide readable/writable storage for the indicated range. The metadata `ref`
arguments must not overlap buffers or each other. Reading never changes input;
writing uses only the supplied value and output buffer.

```csharp
using std;
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
