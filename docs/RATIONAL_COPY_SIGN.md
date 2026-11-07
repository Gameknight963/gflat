# Copying a rational sign

`Rational::CopySign(value, sign)` accepts borrowed pointers and returns an
independent owning fraction with the magnitude of `value` and the sign of
`sign`. A zero sign requests a nonnegative result. Rational zero remains zero
when a negative sign is requested, since rational values have no negative zero.

```csharp
using Standard::Numerics;

int main()
{
    Rational value = new(-7l, 3l);
    Rational sign = new();
    Rational result = Rational::CopySign(&value, &sign);
    Rational expected = new(7l, 3l);
    if (!result.Equals(&expected)) return 1;
    return 0;
}
```

The operation is exact and has no native-integer magnitude limit. Both source
values stay unchanged. Result construction can allocate or throw. On failure,
intermediate owners are released and an assignment destination is unchanged.
