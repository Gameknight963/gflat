# Checked BigInteger integer conversions

BigInteger provides `TryToSByte`, `TryToByte`, `TryToShort`, `TryToUShort`,
`TryToInt`, `TryToUInt`, `TryToNInt`, and `TryToNUInt`, plus matching `To...`
methods. These complement `TryToLong`, `TryToULong`, `ToLong`, and `ToULong`.

```csharp
using Standard::Numerics;

int main()
{
    BigInteger value = BigInteger::Parse("2147483647");
    int result = 0;
    if (!value.TryToInt(ref result) || result != 2147483647) return 1;
    byte small = (byte)99;
    if (value.TryToByte(ref small) || small != (byte)0) return 2;
    uint unsignedValue = value.ToUInt();
    if (unsignedValue != 2147483647u) return 3;
    return 0;
}
```

Try conversions are allocation-free and `nothrow`. Out-of-range values return
false and set the result to zero. Negative values do not fit unsigned types.
Native integer ranges follow the target's pointer width. Result metadata must
not overlap the BigInteger owner or its limb storage.

Throwing conversions report `Exception("BigInteger does not fit in TYPE")`, with
the destination type name replacing TYPE. On failure, an assignment destination
keeps its previous value. Conversion never changes the BigInteger.
