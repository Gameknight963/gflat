# Filling a byte buffer with pseudorandom data

`PseudoRandom.NextBytes` fills a byte pointer/count range or a `Span<byte>`.
The operation advances the generator and does not allocate.

```csharp
using Standard::Memory;
using Standard::Numerics;

int main()
{
    PseudoRandom random = new(42ul);
    byte[16] bytes;
    random.NextBytes(new Span<byte>(&bytes[0], 16));
    PseudoRandom same = new(42ul);
    byte[16] copy;
    same.NextBytes(&copy[0], 16);
    for (nuint i = 0; i < 16; i++)
        if (bytes[i] != copy[i]) return 1;
    return 0;
}
```

The methods are `nothrow` and produce the same bytes as `Fill`: each generated
64-bit word is written from its least significant byte upward. A partial final
word is consumed in full. Splitting a fill across calls can therefore produce a
different byte sequence. An empty destination consumes no random words and
accesses no buffer bytes. No terminator is written.

Freestanding forms are `NextBytes(ref random, destination)` and
`NextBytes(ref random, pointer, count)`. Selective builds add
`Numerics/pseudo_random_bytes.gf` beside `Numerics/PseudoRandom.gf` and the span
sources. The generator is deterministic and unsuitable for secrets, tokens, or
cryptographic keys. Use the platform system-random API for those tasks.
