# Find string bytes outside an allowed set

`String.IndexOfAnyExcept(values, count, start = 0)` finds the first byte at or
after start that is absent from the counted set. `LastIndexOfAnyExcept(values,
count)` finds the last such byte in the whole string. Both return
`String::NotFound` when no byte qualifies. Repeated bytes in the set have no
effect.

```csharp
using Standard::Text;

int main()
{
    String digits = s"1204x7";
    if (digits.IndexOfAnyExcept("0123456789") != 4) return 1;
    if (digits.LastIndexOfAnyExcept("0123456789") != 4) return 2;
    if (digits.IndexOfAnyExcept("0123456789", 10, 5) != String::NotFound) return 3;
    return 0;
}
```

The pointer-only overloads read the set through its first zero byte. Counted
sets can contain zero, and embedded zero bytes in the string are ordinary
searchable data. Positions and counts are byte offsets, including for UTF-8.
These methods do not decode Unicode scalars.

An empty set matches every byte outside it. Forward search returns start when
start is below Length, and reverse search returns Length minus one for a
nonempty string. Empty searched ranges return `String::NotFound`.

Forward search throws when start exceeds Length, before reading the counted
set. Counted empty-set and empty-range searches do not read the set buffer.
The reverse overloads are `nothrow`. Searches allocate no heap storage, permit sets
borrowed from the same String, and leave both inputs unchanged.

The methods use the byte-membership search engine already required by String.
Selective builds need the usual `Text/String.gf` and `Text/ByteSearch.gf`
dependencies.
