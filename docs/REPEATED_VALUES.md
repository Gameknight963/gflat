# Repeated values

`std::Repeated<T>` stores one captured value and a `nuint` count. It describes a
sequence without allocating a buffer of repeated elements.

```csharp
using std;
int main()
{
    Repeated<int> values = new(7, 4);
    if (values.Count != 4 || values.IsEmpty) return 1;
    int sum = 0;
    foreach (int value in values) sum += value;
    if (sum != 28) return 2;
    Repeated<int>::Enumerator cursor = values.GetEnumerator();
    if (!cursor.MoveNext() || cursor.Current != 7) return 3;
    if (values.AsEnumerable().Take(2).Count() != 2) return 4;
    return 0;
}
```

`Count` and `IsEmpty` inspect the stored count in constant time. Both are
immutable and `nothrow`. Each `GetEnumerator()` starts a fresh cursor, with its
own remaining count and a copy of the captured value. `MoveNext()` is `nothrow`
and stays false after exhaustion. Even the maximum `nuint` count is supported:
the cursor decrements a remaining count rather than incrementing past its limit.
Actually consuming that many elements is still expensive; use a bound such as
`Take` when only a prefix is needed.

`Current` returns a value using normal copying rules. It throws before the first
successful `MoveNext()` and after exhaustion. Reading it repeatedly copies the
same captured value without advancing. A failed copy leaves the cursor at that
element, so a caller may retry. It is not a ref property.

## Ownership

Construction copies a named capture or transfers a moved/fresh capture normally.
Sequence copies, cursor copies, and `Current` use the type's ordinary copy
operation. These operations require a copyable `T`; resource owners such as
`String` produce independent copies. Raw and managed pointers copy references,
sharing their target rather than cloning it.

A cursor retains no pointer back to its sequence. Replacing or destroying the
source therefore leaves existing cursors usable. Each cursor owns its capture
until its own destruction, including after exhaustion. A zero-count sequence
still owns its capture; creating its cursor still copies that capture. Plain
scalar values need no allocation, while custom copying can allocate or throw.
Partial copied owners are cleaned up normally on failure.

`GetEnumerator`, `AsEnumerable`, and the `Current` getter are shallow `readonly`
observers. Pointer captures retain their ordinary pointee mutability; deep
immutable receivers keep the language's normal restrictions.

`AsEnumerable()` returns an owning source copy for the existing query API.
Creating or copying queries and their cursors can therefore copy the capture,
even for `Take(0)`. The query's generic `Count()` enumerates; the sequence's
`Count` property simply reads its metadata.

For selective use, include `core/Repeated.gf`, `Collections/IEnumerable.gf`,
`Collections/IEnumerator.gf`, `Collections/Enumerable.gf`, and the chosen query
operator sources. Invalid `Current` access requires ordinary Exception support;
captured owners keep their usual dependencies. No GC is needed unless the
capture itself uses managed references.
