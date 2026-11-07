# BigInteger state properties

`IsZero`, `IsOne`, and `IsEven` inspect an integer without allocating or creating
comparison values. They are `nothrow` and take constant time.

```csharp
using Standard::Numerics;

int main()
{
    BigInteger zero = new();
    BigInteger one = new(1l);
    BigInteger even = BigInteger::Parse("-100000000000000000000000000000000000000");
    if (!zero.IsZero || !zero.IsEven || zero.IsOne) return 1;
    if (!one.IsOne || one.IsEven) return 2;
    if (!even.IsEven || even.IsZero) return 3;
    return 0;
}
```

`IsOne` is true only for positive one. Negative one is neither one nor even.
Zero is even. Parity is independent of sign, so negative even integers also
report `IsEven`. These properties accept immutable and readonly receivers.
`Sign` remains available for distinguishing negative, zero, and positive values.
