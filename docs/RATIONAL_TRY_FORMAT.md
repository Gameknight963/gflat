# Format numeric values into caller storage

`Rational.TryFormat(char* output, nuint capacity, ref nuint written)` writes
canonical decimal text without allocating. `BigInteger` and `Rational` also
accept `TryFormat(Span<char> destination, ref nuint written)`.

```csharp
using Standard::Memory;
using Standard::Numerics;

int main()
{
    Rational value = new(-6l, 8l);
    char[8] buffer;
    Span<char> destination = new(&buffer[0], 8);
    nuint written = 0;
    if (!value.TryFormat(destination, ref written) || written != 4) return 1;
    if (buffer[0] != '-' || buffer[1] != '3' || buffer[2] != '/' || buffer[3] != '4') return 2;
    BigInteger integer = new(123l);
    if (!integer.TryFormat(destination, ref written) || written != 3) return 3;
    return 0;
}
```

Rational text is a signed numerator followed by `/` and the positive denominator.
An integral value omits the denominator. Canonical zero formats as `0`.
No trailing zero terminator is written, and unused destination bytes are unchanged.

Insufficient capacity returns false, sets written to zero, and leaves every
output byte unchanged. Empty spans and zero capacity fail without accessing
output bytes. All overloads are `nothrow`. The output and written storage must
be separate from each other, the numeric owner, and its internal allocations.

The complete required length is checked before any output is written, including
both components and the slash for a fraction. No temporary strings or numeric
copies are constructed. The existing streaming `Format(ITextWriter*)` methods
remain available for sinks.

Selective builds use the normal `Numerics/BigInteger.gf`,
`Numerics/Rational.gf`, and `Memory/Span.gf` dependencies.
