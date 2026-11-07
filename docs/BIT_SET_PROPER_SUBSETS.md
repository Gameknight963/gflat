# Strict BitSet inclusion

`BitSet.IsProperSubsetOf(other)` is true when every set bit in the receiver is
also set in `other`, and `other` has at least one additional set bit.
`IsProperSupersetOf(other)` checks the reverse relation. Equal sets are never
proper subsets or supersets, including when both are empty.

```csharp
using Standard::Collections;

int main()
{
    BitSet first = new(65);
    BitSet second = new(65);
    first[1] = true;
    second[1] = true;
    second[64] = true;
    if (!first.IsProperSubsetOf(&second)) return 1;
    if (!second.IsProperSupersetOf(&first)) return 2;
    if (first.IsProperSubsetOf(&first)) return 3;
    return 0;
}
```

Both bit sets must have matching logical lengths, as with `IsSubsetOf` and
`IsSupersetOf`. A length mismatch throws `Exception("BitSet lengths differ")`
without changing either input. Matching-length checks use packed words, allocate
nothing, and leave input state unchanged.
