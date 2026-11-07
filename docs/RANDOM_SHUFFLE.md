# Shuffling through a random generator

`PseudoRandom.Shuffle<T>(Span<T>)` rearranges an existing span in place.
It uses the same Fisher-Yates implementation as the collection `Shuffle`
function and advances the caller's generator.

```csharp
using Standard::Memory;
using Standard::Numerics;

int main()
{
    PseudoRandom random = new(42ul);
    int[4] values = [0, 1, 2, 3];
    random.Shuffle<int>(new Span<int>(&values[0], 4));
    int total = 0;
    for (nuint i = 0; i < 4; i++) total += values[i];
    if (total != 6) return 1;
    return 0;
}
```

The freestanding generator-first form is `Shuffle<T>(ref random, values)`.
Both forms produce the same permutation and final generator state as
`Standard::Collections::Shuffle<T>(values, ref random)`.

Empty and one-element spans leave the generator unchanged. For larger spans,
each step uses bounded rejection sampling to select a swap, so the number of
raw generated words can vary. The operation allocates no storage and relocates
elements instead of copying them. Noncopyable owning elements are supported.
It takes linear time apart from random-number rejection retries.

Keep the span storage alive and stable throughout the call. Selective builds
load `Numerics/pseudo_random_shuffle.gf`, `Numerics/PseudoRandom.gf`, and
`Collections/span_shuffle.gf` alongside the span sources. The generator is
pseudorandom and is not suitable for cryptographic use.
