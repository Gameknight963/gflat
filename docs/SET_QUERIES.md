# Querying set elements

`HashSet<T>.AsEnumerable()` and `SortedSet<T>.AsEnumerable()` connect a set to
the query operators in `Standard::Queries`.

```csharp
using Standard::Collections;
using Standard::Queries;

int Compare(immutable(int)* left, immutable(int)* right)
{
    if (*left < *right) return -1;
    if (*left > *right) return 1;
    return 0;
}

bool Odd(immutable(int)* value) => *value % 2 != 0;
int Read(immutable(int)* value) => *value;

int main()
{
    SortedSet<int> values = new(Compare);
    values.Add(3);
    values.Add(1);
    values.Add(2);
    Vector<int> odd = values.AsEnumerable().Where(Odd).Select<int>(Read).ToVector();
    if (odd.Count != 2 || odd[0] != 1 || odd[1] != 3) return 1;
    return 0;
}
```

## Borrowing and ordering

Queries yield `immutable(T)*` pointers to stored elements. Obtaining a query and
iterating the set allocate no storage and copy no elements. Noncopyable elements
and immutable sets are supported. `Where` predicates borrow those pointers, and
`Select` can produce independent values when needed. `ToVector()` without a
projection collects the borrowed pointers rather than copying their pointees.

Sorted sets enumerate in comparator order. Hash-set order is unspecified.
Callbacks must not structurally mutate the set during enumeration. Adding,
removing, clearing, or relocating storage invalidates active iterators and
previously yielded element pointers.

## Reusing a query

Each query source retains a pointer to the set object. Keep the set alive at the
same address for the lifetime of saved queries. Moving or destroying the set
invalidates them. A saved query creates a fresh enumerator each time, observing
current contents and storage after earlier additions, removals, or capacity
changes. Merely constructing a query does not call its predicates or selectors.

For explicit source lists, include `Collections/hash_set_queries.gf` or
`Collections/sorted_set_queries.gf`, the corresponding set and its backing map,
`Queries/Enumerable.gf`, and the source files for the query operators used. Basic
set operations do not require these optional query extensions. See
[queries](QUERIES.md) for the available operators and their allocation behavior.
