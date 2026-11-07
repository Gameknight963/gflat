# BigInteger byte conversion

`BigInteger.FromBytes` reads a byte pointer/count range or a `Span<readonly(byte)>`.
`ToBytes()` returns an owning `Vector<byte>`. Both accept `isUnsigned` and
`isBigEndian` flags, which default to false.

For caller-owned output storage, use
[GetByteCount and TryWriteBytes](BIG_INTEGER_WRITE_BYTES.md).

```csharp
using Standard::Collections;
using Standard::Numerics;

int main()
{
    byte[2] input = [(byte)0xfe, (byte)0x80];
    BigInteger signedValue = BigInteger::FromBytes(&input[0], 2);
    if (signedValue.ToLong() != -32514l) return 1;
    BigInteger unsignedValue = BigInteger::FromBytes(&input[0], 2, true);
    if (unsignedValue.ToLong() != 33022l) return 2;
    Vector<byte> bigEndian = unsignedValue.ToBytes(false, true);
    if (bigEndian.Count != 3 || bigEndian[0] != (byte)0 || bigEndian[1] != (byte)0x80 || bigEndian[2] != (byte)0xfe) return 3;
    return 0;
}
```

Signed input uses two's complement. The most significant byte's high bit is
the sign bit. Unsigned input is always nonnegative. Little-endian order stores
the least significant byte first. Big-endian order stores it last. Redundant
zero or sign-extension bytes are accepted, and empty input produces zero.
No pointer bytes are accessed for an empty input.

Export uses the fewest bytes needed for the selected representation. Zero is
one zero byte. Signed positive values gain a leading sign byte when necessary,
so positive 128 is `[0x80, 0x00]` in little-endian signed form. Negative 128 is
`[0x80]`. Negative values cannot be exported as unsigned and throw.

Imports and exports own their results independently of the input. Modifying or
releasing an input buffer does not affect the imported integer. Modifying exported
bytes does not affect the source integer. Allocation failure leaves sources and
an assignment destination unchanged, and temporary storage is released.

The integer stores decimal limbs internally. Conversion uses repeated small-base
arithmetic, with worst-case quadratic work in the input or output byte length.
`ToBytes` allocates a temporary limb copy and the result vector. Import grows the
result's limbs as needed.
