# Writing BigInteger bytes into an existing buffer

`GetByteCount(isUnsigned = false)` returns the number of bytes needed for the
minimal representation. `TryWriteBytes(destination, ref written, isUnsigned = false,
isBigEndian = false)` writes that representation into an initialized `Span<byte>`.
The byte count is the same for either byte order.

```csharp
using Standard::Memory;
using Standard::Numerics;

int main()
{
    BigInteger value = new(128l);
    if (value.GetByteCount() != 2 || value.GetByteCount(true) != 1) return 1;
    byte[4] data = [(byte)165, (byte)165, (byte)165, (byte)165];
    nuint written = 0;
    if (!value.TryWriteBytes(new Span<byte>(&data[1], 2), ref written, false, true)) return 2;
    if (written != 2 || data[0] != (byte)165 || data[1] != (byte)0
        || data[2] != (byte)128 || data[3] != (byte)165) return 3;
    written = 77;
    if (value.TryWriteBytes(new Span<byte>(&data[1], 1), ref written) || written != 0) return 4;
    if (data[1] != (byte)0 || data[2] != (byte)128) return 5;
    return 0;
}
```

The format matches [owning byte export](BIG_INTEGER_BYTES.md): signed values use
two's complement, unsigned values are nonnegative, and zero occupies one zero
byte. Signed positive values gain a sign byte when necessary. For example,
positive 128 needs two signed bytes but one unsigned byte. Negative values with
`isUnsigned = true` throw `Exception`, even for an empty destination.

A successful write returns true, sets `written` to the byte count, and changes
only the destination prefix of that length. A short destination returns false,
sets `written` to zero, and leaves all its bytes unchanged. The destination and
`written` metadata must not overlap one another, the integer object, or its
internal limb storage.

## Allocation and failure behavior

Once the integer exists, magnitudes fitting in `ulong` can be counted and written
without allocating. This includes negative magnitudes through `ulong`'s maximum.
An empty destination returns false without allocating after validating signedness.

Larger values use one temporary decimal-limb buffer. `GetByteCount` consumes a
scratch copy to count bytes. `TryWriteBytes` counts with its scratch copy, restores
that copy using retained capacity, then writes bytes. It does not construct an
intermediate byte vector. This conversion has worst-case quadratic work in the
encoded byte count, like the existing decimal-limb byte export. A short nonempty
destination can still require scratch allocation to determine the exact size.

Allocation failure throws `BadAllocException`. The source and destination bytes
stay unchanged, and `written` remains zero. Other export errors also leave
`written` zero. Scratch storage is released on success, a short result, and
exceptions. Use `ToBytes` when the result itself should own a byte vector.

Selective builds include `core/Numerics/BigInteger.gf` and its existing String,
Vector, and Span dependencies.
