# Deterministic pseudorandom values

`Standard::Numerics::PseudoRandom` is an explicitly seeded SplitMix64 generator. It stores one
64-bit state value, owns no resources, and performs no allocations during normal
generation. It is useful for reproducible tests, procedural content, and sampling.
It is not a cryptographic generator.

```cpp
using Standard::Numerics;
PseudoRandom random = new PseudoRandom(42ul);
int die = random.NextInt(1, 7);
double fraction = random.NextDouble();
char[32] bytes;
random.Fill(bytes, 32);
```

| Method | Result |
| --- | --- |
| `NextULong()` | A full-width 64-bit value. |
| `NextUInt()` | The high 32 bits of a newly generated 64-bit value. |
| `NextInt()` | A full signed 32-bit value from those same high bits, including negative values and both integer extremes. |
| `NextLong()` | A full signed 64-bit value, interpreting one complete generated word as signed. |
| `NextULong(exclusiveMax)` | A value from zero through `exclusiveMax - 1`. |
| `NextUInt(exclusiveMax)` | A value from zero through `exclusiveMax - 1`. |
| `NextInt(exclusiveMax)` | An integer from zero through `exclusiveMax - 1`. |
| `NextInt(inclusiveMin, exclusiveMax)` | An integer in the specified half-open interval, including negative ranges. |
| `NextLong(exclusiveMax)` | A signed 64-bit integer from zero through `exclusiveMax - 1`. |
| `NextLong(inclusiveMin, exclusiveMax)` | A signed 64-bit integer in the specified half-open interval, including negative ranges. |
| `NextDouble()` | A double in `[0, 1)`, constructed from 53 random bits. |
| `NextFloat()` | A float in `[0, 1)`, constructed from 24 random bits. |
| `Fill(char* output, nuint count)` | Exactly `count` bytes written into caller-owned storage. |

`NextInt()` returns the full signed `int` domain, including `int` minimum and
maximum. Unlike .NET's `Random.Next()`, it can return negative values. It uses
the high 32 bits of one newly generated 64-bit word, like `NextUInt()`, then
interprets those bits as signed. Use `NextInt(exclusiveMax)` when a nonnegative
half-open range is wanted. Existing bounded overloads keep their sequences.

Bounded integer methods use rejection sampling to avoid modulo bias. Zero upper
bounds and empty/reversed signed intervals throw `Exception` before advancing
the generator. The full signed `int` span is handled without signed subtraction
overflow.

The seed and sequence are independent of machine byte order and pointer size.
Copying the struct copies its current state, yielding independent identical
streams. Nothing seeds itself from clocks, system entropy, or a shared global
generator.

`Fill` writes the low byte of each generated word first, then the next byte, up
to eight bytes per word. A partial final word is consumed in full. Two separate
calls need not produce the same bytes as one combined call. A zero-count call
does not advance the state. It does not append a null terminator.

The algorithm follows Sebastiano Vigna's
[public-domain SplitMix64 reference](https://prng.di.unimi.it/splitmix64.c).
The first output for seed zero is `0xe220a8397b1dcdaf`.

## Signed 64-bit values

`NextLong()` covers the full signed `long` domain, including both extremes and
negative values. It consumes the same complete word as `NextULong()` and interprets
its bits as signed. It is `nothrow`. Use `NextLong(exclusiveMax)` for a nonnegative
half-open range. Bounded forms use the existing unbiased rejection sampler and
can consume more than one generated word. Zero or negative upper bounds and
empty/reversed intervals throw before advancing the generator. Range arithmetic
supports intervals crossing zero and widths above `long.MaxValue` without signed
overflow. The exclusive endpoint remains excluded: to cover *every* signed bit
pattern, use the parameterless form.

```cpp
using Standard; using Standard::Numerics;

int main()
{
    PseudoRandom random = new(42ul);
    long signedBits = random.NextLong();
    long fileOffset = random.NextLong(5000000000l);
    long timingJitter = random.NextLong(-5000000000l, 5000000001l);
    if (fileOffset < 0l || fileOffset >= 5000000000l) return 1;
    if (timingJitter < -5000000000l || timingJitter >= 5000000001l) return 2;
    return 0;
}
```

These methods change no existing method's sequence, use no additional state or
storage, and allocate nothing during valid generation. They retain PseudoRandom's
existing deterministic, noncryptographic policy.
