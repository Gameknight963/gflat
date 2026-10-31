# Search a query

| Method | Behavior |
| --- | --- |
| `Any(predicate)` | Stop at the first match, or return false |
| `Count(predicate)` | Count every match and return a `nuint` |
| `Contains(&key)` | Stop at the first element equal to the borrowed key |
| `Contains(&key, equal)` | Use an explicit equality callback |

```csharp
using Standard;
using Standard::Memory;
using Standard::Queries;

bool Even(int value) => value % 2 == 0;
bool SameParity(immutable(int)* left, immutable(int)* right)
    => *left % 2 == *right % 2;
bool Same(immutable(int)* left, immutable(int)* right) => *left == *right;
int main()
{
    int[4] values = [1, 2, 3, 4];
    Span<int> view = new(values, 4);
    var query = view.AsEnumerable();
    if (!query.Any(Even)) return 1;
    if (query.Count(Even) != 2) return 5;
    int key = 3;
    if (!query.Contains(&key)) return 2;
    int equivalent = 5;
    if (!query.Contains(&equivalent, SameParity)) return 3;
    if (!view.AsImmutable().AsEnumerable().Contains(&key, Same)) return 4;
    return 0;
}
```

Default Contains requires the matching `IEquatable<T>` contract. The explicit
callback has type `bool(immutable(T)*, immutable(T)*)*`, with the sequence
element first and key second. Use it for another equality relation or a
qualified view whose element type lacks that exact interface.

## Evaluation

The terminal reads `Current` once per tested element. Any and Contains stop
at the first match. Count scans the full sequence and throws
`Exception("Sequence count overflow")` if its result exceeds `nuint`.

Parameterless `Any()` and `Count()` advance without reading the terminal's
`Current`. A preceding filter may still read values to test its predicate.
Short-circuiting can leave an invalid suffix, such as malformed Unicode,
unexamined.

Query searches materialize tested values. Borrowed owning elements may be
copied. Equality callbacks borrow those temporary values only during each call.
Keep the key and source valid throughout the operation. For contiguous spans,
the span Contains algorithm can compare elements directly by borrow.

See [query ownership](QUERIES.md#ownership).
Sources: `Queries/enumerable_predicates.gf`, `Queries/enumerable_count.gf`, and
`Queries/enumerable_contains.gf`, plus EqualityComparer for default equality.
