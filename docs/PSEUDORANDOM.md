# Deterministic pseudorandom values

`Standard::Numerics::PseudoRandom` is a seeded SplitMix64 generator for
reproducible tests, procedural content, and sampling. It is noncryptographic.

```cpp
using Standard::Numerics;

PseudoRandom random = new PseudoRandom(42ul);
int die = random.NextInt(1, 7);
double fraction = random.NextDouble();
char[32] bytes;
random.Fill(bytes, 32);
```

## Choose a range

| Method | Result |
| --- | --- |
| `NextULong()` / `NextUInt()` | Full-width unsigned values |
| `NextLong()` / `NextInt()` | Full-width signed values, including negatives |
| `NextULong(max)` / `NextUInt(max)` | Unsigned value in `[0, max)` |
| `NextLong(max)` / `NextInt(max)` | Signed value in `[0, max)` |
| `NextLong(min, max)` / `NextInt(min, max)` | Value in `[min, max)` |
| `NextDouble()` / `NextFloat()` | Fraction in `[0, 1)` using 53 or 24 random bits |

Bounded integers use rejection sampling to avoid modulo bias. Invalid upper
bounds and empty or reversed intervals throw before advancing the generator.
Signed ranges can cross zero, including widths larger than signed maximum.

Parameterless NextInt and NextLong cover their complete signed domains. Use a
bounded overload for nonnegative values:

```cpp
using Standard;
using Standard::Numerics;

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

## Repeatability and bytes

The seed and sequence are independent of byte order and pointer size. A copy
starts an independent stream at the same current state. NextUInt and NextInt
use the high 32 bits of one generated word. NextULong and NextLong use the
complete word.

`Fill(output, count)` writes low-order bytes first, up to eight per generated
word. A partial final word is consumed in full. Two separate Fill calls can
therefore produce different bytes from one combined call. A zero count keeps
the state unchanged.

The algorithm follows the [SplitMix64 reference](https://prng.di.unimi.it/splitmix64.c).
Seed zero's first output is `0xe220a8397b1dcdaf`.
