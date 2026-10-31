# SortedSet

`Standard::Collections::SortedSet<T>` owns unique values in comparer order.
Storage is contiguous and lookup uses binary search.

```csharp
using Standard;
using Standard::Collections;

int Compare(readonly(int)* left, readonly(int)* right)
{
    if (*left < *right) return -1;
    if (*left > *right) return 1;
    return 0;
}

int main()
{
    SortedSet<int> values = new(Compare);
    values.Add(20);
    values.Add(10);
    int key = 20;
    bool present = values.Contains(&key);
    return 0;
}
```

| Operation | Effect |
| --- | --- |
| `Add(value)` | Inserts, or returns false and destroys the duplicate argument |
| `Contains(&query)` | Tests for a comparer-equivalent value |
| `Find(&query)` | Immutable pointer to the stored equivalent value, or null |
| `Remove(&query)` | Destroys the stored value, returning false when absent |
| `Clear()` | Destroys values and retains capacity |
| `Reserve(count)` | Reserves entry capacity |

A zero comparer result identifies an existing value. The comparer must keep a
consistent ordering and leave the set unchanged through other aliases. Comparison
or allocation failure preserves existing entries. Insertion arguments still follow
normal value cleanup.

Use `move` for noncopyable owners. Enumeration yields immutable pointers in
comparer order. Insertion, removal, and growth can move entries, so reacquire
borrowed pointers and iterators afterward.

## Borrowing the stored equivalent value

Find returns the stored object, which may have different payload fields from an
equivalent query. Duplicate insertion keeps that original object. Keep its owner
alive and retain the pointer only while that entry stays in place.

Source: `core/Collections/SortedSet.gf`
