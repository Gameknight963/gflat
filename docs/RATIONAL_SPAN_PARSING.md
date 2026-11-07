# Parse a rational from a counted text view

`Rational::Parse(Span<readonly(char)>)` and
`TryParse(Span<readonly(char)>, ref Rational)` accept decimal integers or a
numerator/denominator pair. They normalize the result to coprime components,
a positive denominator, and canonical zero.

```csharp
using Standard::Memory;
using Standard::Numerics;

int main()
{
    readonly(char)* input = "[-12/18]";
    Span<readonly(char)> text = new(input + 1, 6);
    Rational parsed = Rational::Parse(text);
    Rational expected = new(-2l, 3l);
    if (!parsed.Equals(&expected)) return 1;
    Rational output = new();
    if (!Rational::TryParse(text, ref output) || !output.Equals(&expected)) return 2;
    return 0;
}
```

The span is counted rather than zero-terminated. Whitespace, embedded zeros,
missing components, extra slashes, and a zero denominator are invalid. An empty
span is invalid without accessing its data pointer.

Invalid TryParse input returns false and replaces output with canonical zero.
Constructing that zero owner can itself allocate and throw. Parse throws for
invalid input. Any allocation failure preserves the old output or assignment
destination. All input reads and candidate construction finish before replacing
output. Successful results own their numeric components independently of the
input buffer.

The overloads use the existing parser without constructing temporary strings.
Selective builds use the normal `Numerics/Rational.gf`, `Numerics/BigInteger.gf`,
and `Memory/Span.gf` dependencies.
