# Minimum and maximum query values

`Min()` and `Max()` return the least or greatest element under the element
type's `IComparable<T>` order. Each also accepts an explicit borrowed comparer:
`Min(compare)` or `Max(compare)`, where `compare` has type
`int(immutable(T)*, immutable(T)*)*`. A negative result orders its first argument
before its second, zero means equal priority, and a positive result orders it
after its second. The comparator must define a consistent order.

```csharp
using Standard; using Standard::Memory; using Standard::Queries;
int main()
{
    int[5] data = [7, -3, 5, -3, 2];
    Span<int> values = new(data, 5);
    if (values.AsEnumerable().Min() != -3) return 1;
    if (values.AsEnumerable().Max() != 7) return 2;
    return 0;
}
```

These terminals enumerate the complete source once and read each terminal
`Current` exactly once. The first element initializes an owned best value.
Each later element is materialized in a local and compared as
`compare(&current, &best)`. It replaces the best only when strictly smaller
for `Min` or strictly larger for `Max`, so the first equal element wins.
A singleton invokes no comparer. An empty source throws
`Exception("Sequence is empty")` without reading `Current` or comparing.

The result follows ordinary `T` value semantics. Borrowed owning elements are
copied when read; replacements move the selected local into the best value and
release the previous best normally. The final result is moved to the caller.
Pointer elements remain pointers and do not acquire ownership of their targets.
The comparer borrows only the two locals during its call; it must not retain
those addresses, mutate their values, or invalidate source iteration through
another alias. Source wrappers retain their normal copy and borrowing rules.

No sorting, sequence buffer, or GC allocation is introduced. The scan uses
constant auxiliary element storage. User constructors, element copies,
comparers, and iterators can allocate or throw. Exceptions propagate and normal
cleanup releases current/best values and the iterator. Assignment to an existing
caller value occurs only after the terminal returns its completed result; a
failed scan does not replace that assignment target.

Default comparison requires the exact closed contract `IComparable<T>` and
forwards through `Comparer<T>::Compare`. It follows the existing floating-point
comparison contract: NaN compares below every non-NaN value and NaNs compare
equal to one another. Thus `Min` selects the first NaN if any are present;
`Max` selects the greatest non-NaN if there is one. Equal signed zeroes retain
the first value. These rules do not promise equivalence to `Math::Min/Max` or
floating-point numeric aggregation APIs with different NaN policies.

The best value is replaced through ordinary local assignment, so its element
type must permit that assignment. Outer `readonly` or `immutable` element types
do not meet this requirement; an explicit comparer alone does not change it.
For a qualified integer view, project to a writable value element before the
scan: `.Select((readonly(int) value) => (int)value).Min()` (likewise `Max`).
This also supplies the existing `IComparable<int>` contract rather than assuming
it applies to `readonly(int)`. The operator does not remove element qualification
automatically.

Selective source users add `Queries/enumerable_extrema.gf` with the normal
sequence contracts and `Collections/Comparer.gf`/`IComparable.gf` for default comparison.
The scan needs no Vector, sorting, or String implementation and makes no direct
libc calls. Ordinary iterator, element, and exception-runtime dependencies still
apply.
