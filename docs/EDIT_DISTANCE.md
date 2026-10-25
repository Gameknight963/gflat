# Counted-byte edit distance

`EditDistance::TryCompute(first, second, scratch, ref distance)` computes the
minimum number of single-byte insertions, deletions, and substitutions needed to
transform the first input into the second. Every operation costs one. This is
[Levenshtein distance](https://xlinux.nist.gov/dads/HTML/Levenshtein.html), useful
for small fuzzy-match candidates when callers control temporary memory.
Transposition is not a single edit. Inputs are counted byte spans: NUL and high
bytes are ordinary values. No Unicode decoding, normalization, or case folding
is applied, so this is not a Unicode-character distance.

```csharp
using std;
int main()
{
    Span<readonly(char)> first = new("kitten", 6);
    Span<readonly(char)> second = new("sitting", 7);
    nuint[7] row;
    Span<nuint> scratch = new(row, 7);
    nuint distance = 99;
    if (!EditDistance::TryCompute(first, second, scratch, ref distance)) return 1;
    if (distance != 3) return 2;
    return 0;
}
```

For two nonempty inputs, scratch needs at least `min(first.Length, second.Length)
+ 1` writable `nuint` elements. The function uses the shorter input for columns,
regardless of argument order. Insufficient scratch returns false before reading
input bytes or writing scratch or `distance`; their prior contents are preserved.
If either input is empty, the result is the other length, no scratch is needed,
and no data buffer is read or written. On success `distance` receives the answer.
Scratch contents become working state and have no public meaning; elements beyond
the required prefix remain unchanged.

Both inputs may overlap one another and must remain alive and unchanged during
the call. Scratch must be separate from both input byte ranges and from all
input/scratch span metadata. The `distance` variable must be separate from those
buffers and metadata as well. Every nonempty span must describe valid readable
input or writable scratch storage as appropriate. The helper does not retain any
pointer and does not validate pointer ownership or overlapping caller storage.

The method is `nothrow` and performs no allocation, formatting, or callbacks.
It takes O(first.Length * second.Length) time and O(min(lengths)) caller-provided
workspace; the implementation never multiplies the lengths. Very long inputs can
therefore still take substantial time. A one-row dynamic program records costs,
with saturating increments to prevent pointer-width arithmetic wraparound in
unused high-cost alternatives. The exact distance is always representable because
it is at most the longer input length.

Explicit source users include `core/EditDistance.gf`, `Span.gf`, and its ordinary
Enumerable/IEnumerable declaration dependencies, along with the normal exception
and runtime declarations required by Span's checked operations. The helper itself
performs no String/GC allocation or libc call, but selecting these files does not
make the complete module freestanding: existing Span constructors and checked
siblings may emit exception support. No Span or compiler behavior is changed.
