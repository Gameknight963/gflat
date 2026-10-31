# Minimum and maximum query values

`Min()` and `Max()` return the first least or greatest element under
`IComparable<T>` ordering. Empty input throws `Exception("Sequence is empty")`.

```csharp
using Standard;
using Standard::Memory;
using Standard::Queries;

int main()
{
    int[5] data = [7, -3, 5, -3, 2];
    Span<int> values = new(data, 5);
    if (values.AsEnumerable().Min() != -3) return 1;
    if (values.AsEnumerable().Max() != 7) return 2;
    return 0;
}
```

For another order, pass a comparer with type
`int(immutable(T)*, immutable(T)*)*`. Negative means the first argument comes
before the second, zero means equal priority, and positive means after.

The scan reads each element once and retains the current best. Equal values
keep the first result. The comparer borrows temporary candidates during its
call. It must not retain those addresses or invalidate the source.

## Floating-point ordering

Default comparison places NaN below every numeric value and treats NaNs as
equal. Min therefore selects the first NaN if one exists. Max selects the
largest non-NaN if available. Equal signed zeroes retain the first value.

## Element types

The best value must support assignment. For an outer readonly or immutable
element type, project to an ordinary value first:
`.Select((readonly(int) value) => (int)value).Min()`.

The scan uses constant element storage. Borrowed owning elements are copied
when read. See [query ownership](QUERIES.md#ownership).

Source: `Queries/enumerable_extrema.gf`, plus `Collections/Comparer.gf` for
default comparison.
