# Integer query sums

`Sum()` consumes a query and returns a checked `long` total. Empty input returns
`0l`. It uses ordinary implicit conversion to `long` when reading each element;
it does not cast or truncate values.

```csharp
using std;
int main()
{
    int[3] data = [2147483647, 1, -2];
    Span<int> numbers = new(data, 3);
    Span<readonly(int)> view = numbers.AsReadOnly();
    long total = view.AsEnumerable().Sum();
    if (total != 2147483646l) return 1;
    return 0;
}
```

The existing integer conversions admit `byte`, `sbyte`, `short`, `ushort`,
`char`, `int`, `uint`, `long`, and `nint`, including readonly and immutable
element views. Their values fit in `long`. Floating point, `ulong`, `nuint`,
`extralong`, bool, and enum values need an explicit caller-chosen conversion or
another reduction. Unsupported elements fail the ordinary assignment check
when the generic body is specialized. No separate numeric interface is required.

Every addition is checked in sequence order, before modifying the total.
If a prefix would exceed the signed 64-bit range, `Sum` throws
`Exception("Sequence sum overflow")`. Later cancellation does not rescue an
overflowing prefix: `[9223372036854775807l, 1l, -1l]` throws at its second value.
Conversely, `[2147483647, 1]` succeeds with `2147483648l`, since integer queries
accumulate in `long`. `IntRange.Sum()` and `FilteredIntRange.Sum()` already use
that result type; converting their output to a general query keeps those totals.

One cursor is constructed. Its `Current` is read once per successful
`MoveNext`; iteration stops at the first source, conversion, or overflow error.
Normal cleanup releases the cursor and copied source on success and failure.
The source collection is not modified, assuming its callbacks and iterator obey
their normal borrowing contracts.

The method itself creates no heap storage on a successful primitive reduction.
Source copies, cursor construction, user conversions, and upstream callbacks
can allocate or throw. `Take(0)` still constructs its input cursor, but does not
advance it. Queries with borrowed storage retain their existing owner-lifetime
and invalidation rules.

Use `Select` before `Sum` for a projection, or `Aggregate` for a different
accumulator type or arithmetic policy. For selective source builds, include
`Collections/EnumerableSum.gf` with the ordinary query contracts and the source
operators you use.
