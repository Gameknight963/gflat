# BigInteger bounds and signs

`BigInteger.Min` and `Max` select the smaller or larger operand. `Clamp` bounds a
value to an inclusive minimum and maximum. `CopySign` combines one operand's
magnitude with the other operand's sign. All operands are borrowed pointers.

```csharp
using Standard::Numerics;

int main()
{
    BigInteger value = new(-30l);
    BigInteger minimum = new(-10l);
    BigInteger maximum = new(20l);
    BigInteger clamped = BigInteger::Clamp(&value, &minimum, &maximum);
    if (clamped.ToLong() != -10l) return 1;
    BigInteger larger = BigInteger::Max(&value, &maximum);
    if (larger.ToLong() != 20l) return 2;
    BigInteger positive = BigInteger::CopySign(&value, &maximum);
    if (positive.ToLong() != 30l) return 3;
    return 0;
}
```

Every successful result owns an independent copy of the selected value or
magnitude. It survives replacement or destruction of the inputs. Inputs may
alias each other or an assignment destination. Zero results need no limb
allocation. Nonzero results allocate their own limb storage.

`Clamp` throws when minimum is greater than maximum, before copying an operand.
Equal bounds are valid. `CopySign` treats a zero sign operand as nonnegative,
and a zero magnitude remains zero regardless of the sign operand.

Allocation failure leaves inputs and an assignment destination unchanged.
Comparisons inspect only existing limbs. Copying takes time proportional to
the selected integer's limb count. These methods belong to the core integer type.
