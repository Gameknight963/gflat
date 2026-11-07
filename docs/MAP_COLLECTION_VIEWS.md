# Enumerating map keys and values

`HashMap<K, V>` and `SortedMap<K, V>` expose `Keys` and `Values` collection views.
Each view provides a live `Count`, `IsEmpty`, and `GetEnumerator` for `foreach`.

```csharp
using Standard::Collections;

int main()
{
    HashMap<int, int> scores = new();
    scores.Add(1, 10);
    scores.Add(2, 20);
    int sum = 0;
    foreach (immutable(int)* value in scores.Values) sum += *value;
    if (sum != 30 || scores.Keys.Count != 2) return 1;
    return 0;
}
```

`Keys` enumerates `immutable(K)*`, and `Values` enumerates `immutable(V)*`.
Pointers borrow the stored objects. Obtaining a view and enumerating it allocate
no storage and copy no keys or values. Noncopyable owners are supported. Views
can be obtained from immutable maps.

Views hold a pointer to the map object. Keep that object alive at the same address.
Destroying or moving the map invalidates saved collection views.
A saved view observes later additions, removals, and clearing. Creating an
iterator after such a change uses the map's current storage, including after rehash.

Structural mutation invalidates active iterators and previously yielded element
pointers. Do not structurally mutate the map while enumerating. Other aliases can change
stored values. View pointers observe those changes, and a new lookup or iterator
is required after storage relocation.

Key and value iteration follow the map's entry order. Hash-map order is
unspecified. Sorted-map order follows its key comparator, even when enumerating
values. An iterator's `Current` throws before its first successful `MoveNext`
and after it reaches the end. These views are part of the core map types.

## Querying a view

`AsEnumerable()` connects either collection view to the query operators in
`Standard::Queries`. It yields the same borrowed pointers and creates no owning
snapshot. A saved query uses the current map contents each time it is enumerated.
The map lifetime and mutation rules above also apply to these queries.

```csharp
using Standard::Collections;
using Standard::Queries;

bool AtLeastTwenty(immutable(int)* value) => *value >= 20;
int Read(immutable(int)* value) => *value;

int main()
{
    HashMap<int, int> scores = new();
    scores.Add(1, 10);
    scores.Add(2, 20);
    Vector<int> selected = scores.Values.AsEnumerable()
        .Where(AtLeastTwenty).Select<int>(Read).ToVector();
    if (selected.Count != 1 || selected[0] != 20) return 1;
    return 0;
}
```

For explicit source lists, include `Collections/map_collection_queries.gf` and
the source files for the query operators you use. Borrowing operations such as
`Where` and `Count` also support noncopyable keys and values. `Select` can create
independently owning results when needed.
