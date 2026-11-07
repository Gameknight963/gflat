# Parse BigInteger from a text span

`BigInteger::Parse(Span<readonly(char)>)` and
`TryParse(Span<readonly(char)>, ref BigInteger)` parse a counted decimal slice
without constructing an intermediate String or requiring a terminator.

```csharp
using Standard::Memory;
using Standard::Numerics;

int main()
{
    readonly(char)* input = "[12345678901234567890]";
    Span<readonly(char)> digits = new(input + 1, 20);
    BigInteger value = BigInteger::Parse(digits);
    BigInteger result = new();
    if (!BigInteger::TryParse(digits, ref result) || !result.Equals(&value)) return 1;
    return 0;
}
```

The grammar matches the pointer/count overloads: an optional leading ASCII
sign followed by decimal digits. Whitespace, separators, and embedded zero
bytes are rejected. An empty span is invalid and its data pointer is not read.

Invalid `TryParse` input returns false and replaces output with zero. Invalid
`Parse` input throws. Constructing a nonzero result can allocate. Allocation
failure throws and preserves existing output, including a Parse assignment
destination. Successful results own their numeric storage independently of
the text buffer.

Selective builds use the normal `Numerics/BigInteger.gf` and `Memory/Span.gf`
dependencies. The new overloads use the existing decimal parser and do not
change its validation or allocation policy.
