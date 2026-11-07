# Inverting every bit

`BitSet.Not()` flips every bit in place and keeps `Length` unchanged. It returns
`void`, matching the other mutating bit-set operations. To create an independent
complement, clone the set before calling `Not()`.

```csharp
using Standard::Collections;

int main()
{
    BitSet flags = new(65);
    flags[0] = true;
    flags[64] = true;
    BitSet complement = flags.Clone();
    complement.Not();
    if (complement.CountSetBits() != 63 || complement[0] || complement[64]) return 1;
    if (!flags[0] || !flags[64]) return 2;
    complement.Not();
    if (!complement.Equals(&flags)) return 3;
    return 0;
}
```

`Not()` is `nothrow` and does not allocate. It takes time proportional to the
number of packed storage words. An empty set stays empty. Unused padding bits
stay clear, and growing the set afterward still initializes new bits to false.
Keep a set unchanged while enumerating it or using it as a stored hash key.
