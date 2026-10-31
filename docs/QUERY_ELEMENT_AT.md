# Query elements by position

| Method | Result |
| --- | --- |
| `ElementAt(index)` | Return the zero-based element, or throw `Exception` with code 3 if it is absent |
| `TryElementAt(index, ref result)` | Replace `result` and return true, or leave it unchanged and return false if the index is absent |

```csharp
using Standard;
using Standard::Memory;
using Standard::Queries;

int main()
{
    int[3] values = [12, 24, 36];
    Span<int> source = new(values, 3);
    int second = source.AsEnumerable().ElementAt(1); // 24
    int output = 99;
    if (!source.AsEnumerable().TryElementAt(1, ref output) || output != 24) return 1;
    if (source.AsEnumerable().TryElementAt(3, ref output) || output != 24) return 2;
    return 0;
}
```

Both methods enumerate only as far as the selected position, even for sources
that also support indexing. The terminal reads `Current` only at that position.
For example, `.Select(project).ElementAt(2)` calls `project` once. A preceding
`Where` still reads candidates to test its predicate.

Results use value semantics. An owning String read from borrowed storage is
copied. Pointer results retain the source's lifetime requirements.

`TryElementAt` can still throw on input, copying, or callback failure. Those
errors preserve its previous output. For an outer `readonly(T)` or
`immutable(T)` element type, use `ElementAt`: `ref T` also qualifies the output
slot and prevents replacement. A readonly pointee alone leaves the pointer
slot writable.

Source: `Queries/enumerable_element_at.gf`.
