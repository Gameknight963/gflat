# Query set operations by key

`UnionBy`, `ExceptBy`, and `IntersectBy` compare selected keys while yielding
whole input elements. Only the key needs `IHashable` and `IEquatable<K>`.

| Operation | Second input | Result order |
| --- | --- | --- |
| `first.UnionBy(second, selector)` | Elements of the same type as `first` | First element for each key, reading `first` before `second` |
| `first.ExceptBy(keys, selector)` | Keys | First element for each key absent from `keys` |
| `first.IntersectBy(keys, selector)` | Keys | First element for each key present in `keys` |

Freestanding forms such as `ExceptBy(first, keys, selector)` are also available.
The selector has type `K(T)*`. It runs once for each input element examined,
including duplicates. `ExceptBy` and `IntersectBy` do not apply it to the second
input, which already supplies keys.

```csharp
using Standard::Collections;
using Standard::Memory;
using Standard::Queries;

struct Record
{
    public int Id;
    public int Score;
}

int Key(Record value) => value.Id;

int main()
{
    Record[3] records = default(Record[3]);
    records[0].Id = 2; records[0].Score = 20;
    records[1].Id = 1; records[1].Score = 10;
    records[2].Id = 2; records[2].Score = 99;
    int[1] selected = [2];
    Span<Record> input = new(records, 3);
    Span<int> keys = new(selected, 1);

    Vector<Record> combined = input.AsEnumerable().UnionBy(input.AsEnumerable(), Key).ToVector();
    Vector<Record> difference = input.AsEnumerable().ExceptBy(keys.AsEnumerable(), Key).ToVector();
    Vector<Record> common = input.AsEnumerable().IntersectBy(keys.AsEnumerable(), Key).ToVector();
    if (combined.Count != 2 || combined[0].Score != 20 || combined[1].Score != 10) return 1;
    if (difference.Count != 1 || difference[0].Score != 10) return 2;
    if (common.Count != 1 || common[0].Score != 20) return 3;
    return 0;
}
```

## Evaluation and storage

These are lazy queries. Constructing a query over borrowed views does not read
input elements or call the selector. Each `GetEnumerator()` opens both input
iterators and creates an independent hash set.

`UnionBy` retains distinct selected keys as it visits both sequences.
`ExceptBy` and `IntersectBy` consume the entire second sequence on their first
`MoveNext()`, before visiting the first. That second sequence must be finite,
even when the first is empty. `ExceptBy` adds newly yielded keys to the set.
`IntersectBy` removes matching keys so each appears at most once.

The set owns its keys, including projected owning values. The result's `Current`
comes from the input iterator. Keep borrowed input owners alive and their
storage stable while using the query. Retained keys must keep stable hashing
and equality. Iterators and keys are released when the result enumerator leaves
scope, including early exit and exception unwinding.

Selector, iterator, copying, hashing, equality, and allocation errors propagate.
A failed `MoveNext()` ends that enumerator. `Current` is unavailable before the
first successful advance, after completion, and after a failed advance.
