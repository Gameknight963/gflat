# Retrieve stored set values

`HashSet<T>` and `SortedSet<T>` have optional copying
`TryGetValue(&query, ref output)` extensions. They return true after copying
the stored equivalent value into initialized output. A missing value returns
false and preserves output.

This retrieves the representative retained by the set, including payload
fields that do not participate in equality or ordering.

```csharp
using Standard;
using Standard::Collections;
using Standard::Text;

struct Record : IHashable, IEquatable<Record>
{
    public int Id;
    public String Label;
    public Record(int id, String label) { Id = id; Label = move label; }
    public immutable nuint GetHashCode() => (nuint)Id;
    public immutable bool Equals(immutable(Record)* other) => Id == other.Id;
}

int Compare(immutable(Record)* left, immutable(Record)* right) => left.Id - right.Id;

int main()
{
    HashSet<Record> records = new();
    records.Add(new Record(1, s"stored"));
    records.Add(new Record(1, s"duplicate"));
    Record query = new(1, s"query");
    Record actual = new(99, s"previous");
    if (!records.TryGetValue(&query, ref actual) || !actual.Label.Equals("stored")) return 1;
    records.Clear();
    if (!actual.Label.Equals("stored")) return 2;

    SortedSet<Record> ordered = new(Compare);
    ordered.Add(new Record(3, s"last"));
    ordered.Add(new Record(1, s"first"));
    if (ordered.Min.Id != 1 || ordered.Max.Id != 3) return 3;
    return 0;
}
```

## Copying and errors

`T` must be copyable for `TryGetValue`. A hash set uses its hashing and equality
contracts, and a sorted set uses its comparer. The query is borrowed.

Lookup and copying finish before output is replaced. Lookup, copy, or allocation
errors preserve output and the set. A miss does not copy or construct an
element. Success follows the type's ordinary copy semantics, including owning
copies when defined by `T`.

Output must be separate from the set's stored elements. Writing to a stored
key could invalidate hashing or ordering. The query can alias the separate
output variable because it is not used after replacement.

Use `Find(&query)` to borrow an immutable pointer to the stored representative
instead of copying it, including for noncopyable types.

## Sorted endpoints

`SortedSet<T>.Min` and `.Max` return `ref immutable(T)` borrows of the first and
last stored values in comparer order. With a descending comparer, `Min` is
the numerically largest value. Both properties throw on an empty set.

On a nonempty set, endpoint access takes constant time, performs no copying or
allocation, and does not invoke the comparer. It supports noncopyable elements.
The key cannot be modified through the returned borrow. Keep the set alive and reacquire
borrows after mutations that move or remove its entries.

## Source selection

`Min` and `Max` are part of `Collections/SortedSet.gf`. The copying lookup
extensions are separate:

| Set | Extension source |
| --- | --- |
| `HashSet<T>` | `Collections/hash_set_lookup.gf` |
| `SortedSet<T>` | `Collections/sorted_set_lookup.gf` |

The freestanding form is `TryGetValue(&set, &query, ref output)`.
