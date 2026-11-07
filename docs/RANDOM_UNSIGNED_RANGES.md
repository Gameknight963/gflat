# Unsigned random ranges

`PseudoRandom.NextUInt(inclusiveMin, exclusiveMax)` and
`NextULong(inclusiveMin, exclusiveMax)` return unsigned values within the given
half-open interval. Equal or reversed bounds throw before consuming random state.

```csharp
using Standard::Numerics;

int main()
{
    PseudoRandom random = new(42ul);
    uint value = random.NextUInt(3000000000u, 4000000000u);
    if (value < 3000000000u || value >= 4000000000u) return 1;
    ulong wide = random.NextULong(9223372036854775808ul, 18446744073709551615ul);
    if (wide < 9223372036854775808ul || wide == 18446744073709551615ul) return 2;
    return 0;
}
```

These overloads use rejection sampling through the existing bounded generator,
without allocation or modulo bias. A call may consume more than one random word.
For the same seed, a two-bound draw consumes the same state as a draw with upper
bound `exclusiveMax - inclusiveMin`, then adds the lower bound.

The upper endpoint is excluded even when it is the type's maximum value. Use
parameterless `NextUInt()` or `NextULong()` to draw from the entire unsigned type.
The generator is deterministic and is not intended for cryptographic use.
