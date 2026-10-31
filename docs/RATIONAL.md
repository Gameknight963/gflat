# Exact rational values

`Standard::Numerics::Rational` owns a numerator and denominator backed by BigInteger. Every
successfully constructed value has coprime parts, a positive denominator, and
canonical zero `0/1`. Arithmetic preserves exact fractions within available storage.

```csharp
using Standard::Text;
using Standard; using Standard::Numerics;

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
ordinary value-copy rules. Use `move` to transfer existing owners. A negative
denominator reverses both signs. Zero denominators throw
`Exception("Rational denominator is zero")` before normalization. Long minimum
values are handled through BigInteger, without negating them in primitive space.
The default constructor allocates the ordinary BigInteger denominator `1`. It
does not rely on static heap storage or a special zero-denominator sentinel.

`Add`, `Subtract`, `Multiply`, and `Divide` borrow an immutable pointer to the
other Rational and return a new normalized owner. `Negate()` also returns a new
owner. Division by a zero Rational throws `Exception("Rational divisor is zero")`.
Receivers and borrowed operands are unchanged, including when both pointers
refer to the same value. There are no arithmetic operators.

Copying performs a deep copy of both BigIntegers. `Numerator` and `Denominator`
return independent owning value copies, rather than pointers into the Rational.
They may allocate and throw. Moving uses ordinary ownership transfer. Borrowed
operand pointers must stay valid. Do not move or destroy their owners during calls.

`Equals`, `CompareTo`, and `GetHashCode` use canonical numeric value. Equivalent
fractions compare equal and hash equally. Comparison uses exact cross-products.
Arithmetic likewise uses straightforward BigInteger products before GCD reduction.
Intermediate values can be larger than the result. Ordinary BigInteger size and
allocation limits apply.
Failures clean partial owners, and failed result assignment preserves its old
destination. Arithmetic methods do not mutate their operands.

## Parsing integer and fraction text

`Parse(text, count)` and `Parse(text)` accept an integer or one
`numerator/denominator` pair. Each part uses BigInteger's strict optional `+`/`-`
followed by ASCII decimal digits. Leading zeros and negative denominators are
allowed. Whitespace, empty parts, multiple slashes, decimal points, radix prefixes,
non-ASCII digits and trailing bytes are rejected. A zero denominator is invalid
input. Successful results use the same canonical representation as constructors.
`Parse` throws `Exception("Invalid rational")` for malformed input.

`TryParse(text, count, ref result)` and `TryParse(text, ref result)` return true
for accepted text and replace `result` with its normalized owning value. False
replaces it with canonical `0/1`. These methods are **not** `nothrow`: even a
false result can allocate the ordinary BigInteger denominator `1`. If allocation
or another construction operation throws, the old result is preserved, rather
than partially reset. Syntax errors remain false when constructing that reset
value succeeds.

Counted input includes every supplied byte, so embedded NUL is invalid. CString
overloads stop at the first NUL and require a readable terminator. Input is
borrowed for the call. All reads and candidate construction complete before
replacing the output, so fully readable input in the result's owned storage is
also supported. No input pointer is retained. The caller must keep the bytes
valid until parsing returns. No encoding normalization or whitespace trimming
is performed.

Formatting writes the integer numerator when the denominator is `1`, otherwise
`numerator/denominator`, with decimal BigInteger spelling. It adds no whitespace,
newline, terminator, or flush. The existing generic `ToString()` extension returns
an owning String for a named Rational. It borrows the receiver. Writer failures
propagate and may leave a prefix in the sink without altering the Rational.

For explicit source selection include `Numerics/Rational.gf`, BigInteger's existing
dependencies, and `Text/formattable_string.gf` only when using the ToString extension.
