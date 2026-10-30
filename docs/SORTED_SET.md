# SortedSet

`Standard::Collections::SortedSet<T>` owns unique values in comparator order. It wraps
`SortedMap<T, byte>` and therefore stores its entries contiguously. Lookups use
binary search; insertion and removal may move later entries. No hash or equality
interface is required, and noncopyable owning values work.

```csharp
using Standard; using Standard::Collections;

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

The comparison must define a consistent ordering. A result of zero identifies
an existing value, even when the two objects have different fields. `Add(value)`
returns false for a duplicate and destroys the unused argument without replacing
the stored owner. Use `move` to transfer an existing noncopyable value. `Remove`
borrows its query and destroys a matching stored value; it returns false on a
miss. `Clear` destroys all values while retaining capacity. `Reserve`, `Count`,
`Capacity`, and `IsEmpty` follow the other owning collections.

`Contains` and `GetEnumerator` work on immutable receivers. The nested
`SortedSet<T>::Enumerator` borrows the storage and exposes `immutable(T)* Current`
in comparator order. Keys cannot be modified through that pointer. The source
must remain alive and at a stable address; do not mutate, move, or destroy it
while iterating. Structural mutations may invalidate borrowed pointers. If a
removal query points into this set, lookup finishes before the stored owner is
destroyed or moved.

A throwing comparison or failed insertion/reserve allocation leaves existing
values and their addresses unchanged. An insertion argument is still consumed
and destroyed if insertion fails. Copying is not required for insertion,
relocation, lookup, or enumeration. Callbacks must not mutate the set through
another alias. Sets do not own objects pointed to by pointer-valued elements.

There are no cross-set relation or mutation methods yet: sets can use different
comparisons, so such operations need an explicit compatibility contract first.
Source-library selection requires `Collections/SortedSet.gf`, `Collections/SortedMap.gf`, `Collections/Vector.gf`,
`Memory/Span.gf`, and their query interface declarations. No tree nodes or GC allocations
are introduced. The selected `Memory` implementation supplies vector storage.


## Borrowing the stored equivalent value

`Find(immutable(T)* query)` returns `immutable(T)*?` borrowing the actual
comparison-equivalent stored value, or null when absent. It delegates to the
underlying sorted map's FindKey lookup, taking logarithmic comparisons without
copying owners or allocating storage. Comparator exceptions propagate without
library mutation under the existing callback rules. A query may borrow the
stored value. The result protects reachable key state and does not extend its
lifetime: keep the owner alive, and reacquire borrows affected by growth or shifts
from insertion/removal. Removing a value, clearing or destroying its owner ends
that value's borrow. Equivalent duplicate insertion keeps the original payload.
