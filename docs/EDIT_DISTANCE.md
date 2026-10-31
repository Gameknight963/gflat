# Counted-byte edit distance

`Standard::Text::EditDistance::TryCompute` calculates
[Levenshtein distance](https://xlinux.nist.gov/dads/HTML/Levenshtein.html): the
minimum number of byte insertions, deletions, and substitutions transforming one
input into the other. Each operation costs one. Swapping different adjacent bytes costs
two edits.

```csharp
using Standard;
using Standard::Text;
using Standard::Memory;

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

| Parameter | Meaning |
| --- | --- |
| `first`, `second` | Readonly character spans to compare |
| `scratch` | Writable span of at least `min(lengths) + 1` `nuint` elements |
| `distance` | Result on success |

Insufficient scratch returns false and preserves scratch and result. If either
input is empty, the answer is the other length and scratch is unnecessary.

The inputs may overlap each other. Keep scratch and result separate from inputs
and their span metadata. Scratch becomes working state during the call.

Work is O(first length × second length), with one row of scratch. Matching is by
byte, so a UTF-8 scalar may account for several edits.

Source: `core/Text/EditDistance.gf`
