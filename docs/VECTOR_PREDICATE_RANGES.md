# Searching vector ranges with predicates

Load `Collections/vector_predicates.gf` for `FindIndex(start, predicate)` and
`FindIndex(start, count, predicate)`. The callback borrows each element as
`immutable(T)*`, so noncopyable elements can be searched directly.

```csharp
using Standard::Collections;

bool Positive(immutable(int)* value) => *value > 0;

int main()
{
    Vector<int> values = new();
    values.Add(-5); values.Add(7); values.Add(8); values.Add(9);
    if (values.FindIndex(1, 2, Positive) != 1) return 1;
    if (values.FindIndex(2, 0, Positive) != values.Count) return 2;
    return 0;
}
```

The first match returns its absolute vector index. No match returns the full
vector's `Count`. Starting-index overloads search through the end, while counted
forms search only the selected forward range. An empty range at `Count` is valid
and makes no predicate calls. Invalid ranges throw before calling the predicate.

The helper allocates nothing and copies no elements. Callbacks must leave the
vector's structure stable. If a predicate throws, search stops immediately and
the helper leaves the vector unchanged. Earlier external callback effects remain.
Free-function forms under `Standard::Collections` accept an
`immutable(Vector<T>)*` before the range arguments.
