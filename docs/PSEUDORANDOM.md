# Deterministic pseudorandom values

`std::PseudoRandom` is an explicitly seeded SplitMix64 generator. It stores one
64-bit state value, owns no resources, and performs no allocations during normal
generation. It is useful for reproducible tests, procedural content, and sampling.
It is not a cryptographic generator.

```cpp
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
| `NextULong(exclusiveMax)` | A value from zero through `exclusiveMax - 1`. |
| `NextUInt(exclusiveMax)` | A value from zero through `exclusiveMax - 1`. |
| `NextInt(exclusiveMax)` | An integer from zero through `exclusiveMax - 1`. |
| `NextInt(inclusiveMin, exclusiveMax)` | An integer in the specified half-open interval, including negative ranges. |
| `NextDouble()` | A double in `[0, 1)`, constructed from 53 random bits. |
| `NextFloat()` | A float in `[0, 1)`, constructed from 24 random bits. |
| `Fill(char* output, nuint count)` | Exactly `count` bytes written into caller-owned storage. |

Bounded integer methods use rejection sampling to avoid modulo bias. Zero upper
bounds and empty/reversed signed intervals throw `Exception` before advancing
the generator. The full signed `int` span is handled without signed subtraction
overflow.

The seed and sequence are independent of machine byte order and pointer size.
Copying the struct copies its current state, yielding independent identical
streams. Nothing seeds itself from clocks, system entropy, or a shared global
generator.

`Fill` writes the low byte of each generated word first, then the next byte, up
to eight bytes per word. A partial final word is consumed in full; two separate
calls need not produce the same bytes as one combined call. A zero-count call
does not advance the state. It does not append a null terminator.

The algorithm follows Sebastiano Vigna's
[public-domain SplitMix64 reference](https://prng.di.unimi.it/splitmix64.c).
The first output for seed zero is `0xe220a8397b1dcdaf`.
