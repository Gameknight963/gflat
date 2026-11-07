# Select integer values by magnitude

`BigInteger::MinMagnitude(first, second)` returns a copy of the input with the
smaller absolute value. `MaxMagnitude` selects the larger absolute value.
Both preserve the selected input's sign. On equal magnitudes, minimum selects
the negative value and maximum selects the positive value.

```csharp
using Standard::Numerics;

int main()
{
    BigInteger negative = new(-7l);
    BigInteger positive = new(7l);
    BigInteger smaller = BigInteger::MinMagnitude(&positive, &negative);
    BigInteger larger = BigInteger::MaxMagnitude(&negative, &positive);
    if (smaller.ToLong() != -7l || larger.ToLong() != 7l) return 1;
    return 0;
}
```

Arguments are borrowed. The result owns its storage independently and can
outlive either input. Inputs can refer to the same value, and a result can be
assigned back to either input.

Selection compares the existing magnitude limbs without allocating temporary
absolute values. Copying a nonzero selected result can allocate and throw.
Failure leaves the inputs and an assignment destination unchanged. Selecting
zero requires no allocation.

Selective builds need the normal `Numerics/BigInteger.gf` dependencies.
