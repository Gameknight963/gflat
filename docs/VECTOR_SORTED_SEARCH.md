# Searching sorted vectors

`Vector<T>.BinarySearch` finds the first comparison-equivalent element, or returns
`Count` when none exists. `LowerBound` finds the first element not less than the
key. `UpperBound` finds the first element greater than it. `EqualRange` borrows all
equivalent elements as a `Span<immutable(T)>`.

```csharp
using Standard::Collections;
using Standard::Memory;

int main()
{
    Vector<int> values = new();
    values.Add(2);
    values.Add(5);
    values.Add(5);
    values.Add(9);
    int key = 5;
    if (values.BinarySearch(&key) != 1) return 1;
    if (values.LowerBound(&key) != 1 || values.UpperBound(&key) != 3) return 2;
    Span<immutable(int)> matches = values.EqualRange(&key);
    if (matches.Length != 2) return 3;
    key = 7;
    if (values.BinarySearch(&key) != values.Count || values.LowerBound(&key) != 3) return 4;
    return 0;
}
```

Every operation accepts an optional `start, count` pair before the key to search
a subrange. Bounds are validated before comparisons. An empty range at `Count`
is valid. Invalid ranges throw. Index results are absolute indices in the vector.
A lower or upper bound can equal the end of the searched range. A failed binary
search always returns the full vector `Count`, even when searching a subrange.
Use a bound operation for insertion positions, rather than a complemented index.

Default ordering requires `IComparable<T>`. Each method also accepts a final
borrowed comparator. The selected range must already be sorted using that same
consistent ordering. Descending and other custom orders work when sorting and
searching use the same comparator. The algorithms do not validate sortedness.

Elements and keys are borrowed, not copied, and noncopyable owners are supported.
The source may be immutable. The key may point into the source. Searches allocate
no storage and perform logarithmic comparisons. Comparator exceptions propagate
before assigning a result. Do not mutate source storage or keys from comparisons.

An equal-range result borrows the vector's storage. Keep the vector alive and
reacquire the view after structural mutation or relocation. Empty results have
null `Data`. Other aliases can still change the underlying elements.

Selective builds use `Collections/vector_sorted_search.gf` and
`Collections/span_algorithms.gf`. Default ordering also uses `Collections/Comparer.gf`.
Freestanding forms take an immutable vector pointer as their first argument.
