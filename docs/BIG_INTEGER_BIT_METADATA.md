# Inspect an integer's binary size

`BigInteger.GetBitLength()` returns a `long` containing the shortest
two's-complement bit length, excluding the sign bit. Positive values use their
ordinary binary length. For negative values, the result is the binary length of
`abs(value) - 1`. Zero and minus one both return zero.

`IsPowerOfTwo` is true for positive powers of two, including one. Zero and all
negative values return false.

```csharp
using Standard::Numerics;

int main()
{
    BigInteger positive = new(256l);
    BigInteger negative = new(-256l);
    if (positive.GetBitLength() != 9l || !positive.IsPowerOfTwo) return 1;
    if (negative.GetBitLength() != 8l || negative.IsPowerOfTwo) return 2;
    return 0;
}
```

Both operations borrow the receiver. Magnitudes fitting in `ulong` allocate
nothing, and `IsPowerOfTwo` rejects nonpositive values without allocating.
Larger magnitudes need one temporary limb buffer because the stored limbs use
base 1,000,000,000. Scratch allocation failure throws `BadAllocException` and
preserves the receiver. Conversion work is quadratic in the number of limbs.
`GetBitLength` throws if its result exceeds `long.MaxValue`.

Selective builds include `Numerics/BigInteger.gf` and its normal text,
collection, and memory dependencies.
