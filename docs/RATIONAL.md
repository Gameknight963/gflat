# Exact rational values

`Standard::Numerics::Rational` stores an exact fraction using two BigIntegers.
Values are reduced to coprime parts with a positive denominator. Zero is `0/1`.

```csharp
using Standard::Text;
using Standard;
using Standard::Numerics;

int main()
{
    Rational first = new(1l, 6l);
    Rational second = new(1l, 3l);
    Rational sum = first.Add(&second);
    Rational expected = new(1l, 2l);
    if (!sum.Equals(&expected)) return 1;
    String text = sum.ToString();
    if (!text.Equals("1/2")) return 2;
    return 0;
}
```

## Construction and arithmetic

Construct from a signed long, two signed longs, or two BigInteger values.
Default construction gives `0/1`. A negative denominator reverses both signs.
A zero denominator throws `Exception("Rational denominator is zero")`.

BigInteger arguments follow value semantics. Use `move` to transfer existing
owners. `Numerator` and `Denominator` return independent BigInteger copies.

`Add`, `Subtract`, `Multiply`, and `Divide` borrow the other Rational and return
a new normalized value. `Negate()` returns its opposite. Division by a zero
Rational throws `Exception("Rational divisor is zero")`.

Equality and hashing use the reduced value. Comparison uses exact
cross-products. Intermediate products can be larger than the final result,
so BigInteger storage and allocation limits apply.

## Parsing integer and fraction text

`Parse` and `TryParse` accept a signed decimal integer or one
`numerator/denominator` pair. Leading zeroes and negative denominators work.
Whitespace, empty parts, extra slashes, radix prefixes, decimal points, and
zero denominators are invalid. Counted input also rejects embedded NULs.

`TryParse(text, count, ref result)` and the C-string overload replace the result
with the normalized fraction on success. Malformed input returns false and
resets it to `0/1`. Even that reset can allocate for denominator `1`. Allocation
failure throws and preserves the old result. Input can borrow the result's old
owned storage because replacement happens after reading and construction.

`Parse` throws `Exception("Invalid rational")` for malformed input.

## Formatting

Formatting writes `numerator/denominator`, omitting `/1` for integers. The
generic ToString extension returns an owning String. Writer errors can leave
a partial result in the sink.

Sources: `Numerics/Rational.gf`, BigInteger dependencies, and
`Text/formattable_string.gf` for the ToString extension.
