# Exact rational values

`std::Rational` owns a numerator and denominator backed by BigInteger. Every
successfully constructed value has coprime parts, a positive denominator, and
canonical zero `0/1`. This is exact fraction arithmetic, without a fixed-width
overflow representation, floating-point approximation, or special infinity/NaN.

```csharp
using std;

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

Constructors accept no arguments (`0/1`), one signed long (an integer), a pair
of signed longs, or a pair of owning BigInteger values. BigInteger arguments use
ordinary value-copy rules; use `move` to transfer existing owners. A negative
denominator reverses both signs. Zero denominators throw
`Exception("Rational denominator is zero")` before normalization. Long minimum
values are handled through BigInteger, without negating them in primitive space.
The default constructor allocates the ordinary BigInteger denominator `1`; it
does not rely on static heap storage or a special zero-denominator sentinel.

`Add`, `Subtract`, `Multiply`, and `Divide` borrow an immutable pointer to the
other Rational and return a new normalized owner. `Negate()` also returns a new
owner. Division by a zero Rational throws `Exception("Rational divisor is zero")`.
Receivers and borrowed operands are unchanged, including when both pointers
refer to the same value. There are no operators or parser in this API.

Copying performs a deep copy of both BigIntegers. `Numerator` and `Denominator`
return independent owning value copies, rather than pointers into the Rational.
They may allocate and throw. Moving uses ordinary ownership transfer. Borrowed
operand pointers must stay valid; do not move or destroy their owners during calls.

`Equals`, `CompareTo`, and `GetHashCode` use canonical numeric value. Equivalent
fractions compare equal and hash equally. Comparison uses exact cross-products;
arithmetic likewise uses straightforward BigInteger products before GCD reduction.
Intermediate values can be larger than the result; ordinary BigInteger size and
allocation limits apply.
Failures clean partial owners, and failed result assignment preserves its old
destination. No method mutates an existing Rational's parts.

Formatting writes the integer numerator when the denominator is `1`, otherwise
`numerator/denominator`, with decimal BigInteger spelling. It adds no whitespace,
newline, terminator, or flush. The existing generic `ToString()` extension returns
an owning String for a named Rational; it borrows the receiver. Writer failures
propagate and may leave a prefix in the sink without altering the Rational.

For explicit source selection include `Rational.gf`, BigInteger's existing
dependencies, and `FormattableString.gf` only when using the ToString extension.
