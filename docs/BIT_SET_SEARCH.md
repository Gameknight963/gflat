# Searching packed bits

`FindIndex(value, start = 0)` searches forward from `start`.
`FindLastIndex(value)` searches backward through the whole set.
`FindLastIndex(value, endExclusive)` searches only indices below `endExclusive`.
All return `Length` when no bit matches, including an empty search range.
An endpoint greater than `Length` throws without changing the set.

```csharp
using Standard::Collections;

int main()
{
    BitSet occupied = new(65);
    occupied[31] = true;
    occupied[64] = true;
    if (occupied.FindLastIndex(true) != 64) return 1;
    if (occupied.FindLastIndex(true, 64) != 31) return 2;
    if (occupied.FindLastIndex(false) != 63) return 3;
    if (occupied.FindLastIndex(true, 0) != occupied.Length) return 4;
    return 0;
}
```

Searches inspect packed words without allocating or modifying bits. Padding
outside the logical length is excluded. To enumerate matches backward, use each
found index as the next `endExclusive` and stop when the result is `Length`.
