# Count sequence elements by key

`CountBy(selector)` consumes a query and returns an owning `HashMap<K, nuint>`
containing the number of elements for each selected key. The selector has type
`K(T)*`. Keys require `IHashable` and the matching `IEquatable<K>` contract,
following the existing HashMap rules. Hash values and equality must agree and
remain stable while a key is stored. Map enumeration order is unspecified.

```csharp
using std; using std::Queries; using std::Collections;

int Parity(int value) => value % 2;

int main()
{
    int[6] values = [1, 2, 3, 2, 4, 1];
    Span<int> source = new(&values[0], 6);
    HashMap<int, nuint> counts = source.AsEnumerable().CountBy(&Parity);
    int odd = 1;
    int even = 0;
    if (counts.Count != 2 || counts[&odd] != 3 || counts[&even] != 3) return 1;
    return 0;
}
```

The terminal iterator's `Current` is read once per element, followed by one
selector call, in source order. Both the local element and the by-value selector
argument follow ordinary copy/move rules; these copies may allocate or throw.
The selector result becomes a local key. A new key moves into the map. For an
equal key, its existing counter increments and the temporary key is destroyed.
The first stored key remains the representative of its equality class.

The returned map owns its keys and counters independently of the query. A key
that itself contains borrowed pointers retains its ordinary lifetime obligations;
do not return a key borrowing a temporary selector argument. The query receiver
and source follow ordinary value-copy/move rules on entry. Borrowed source
adapters retain their backing-owner lifetime requirements, and callbacks must
respect the source's existing mutation and lifetime rules.
The operator does not alter the source's elements or dispose resources borrowed
by a source adapter. Source values and iterators are destroyed normally.

An empty source returns an empty map without allocating internally or invoking
the selector. A source's own iterator construction or advancement can allocate.
Other inputs allocate for map storage and may allocate for element/key copies.
Before incrementing `nuint` maximum, the operator throws
`Exception("Sequence count overflow")`, matching `Count()`.

Iterator, selector, hashing, equality, capacity, and allocation failures propagate.
The partial map, local key/element, and iterator are destroyed during unwinding.
Assigning the failed result to an existing map leaves that destination unchanged;
external side effects and consumed source input are not rolled back.

For explicit source selection include `Queries/enumerable_count_by.gf`, the
normal Enumerable interfaces, and HashMap's hashing/equality dependencies.
