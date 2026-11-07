# Summing numeric sequences

Load `Standard/core/Queries/enumerable_sum.gf` for `Sum()` and selector forms.

```csharp
using Standard::Memory;
using Standard::Queries;

int Square(int value) => value * value;

int main()
{
    int[3] values = [2, 4, 6];
    Span<int> span = new(&values[0], 3);
    if (span.AsEnumerable().Sum() != 12l) return 1;
    if (Sum(span.AsEnumerable(), Square) != 56l) return 2;
    double[2] fractions = [0.25, 0.5];
    Span<double> parts = new(&fractions[0], 2);
    if (parts.AsEnumerable().Sum() != 0.75) return 3;
    return 0;
}
```

The existing generic integer form accepts elements implicitly convertible to
`long`. Each signed 64-bit prefix is checked, and the result is `long`.
Selectors returning `int` or `long` use that same checked accumulator and
result type. A prefix overflow throws even if later values would cancel it.
This deliberately retains the library's wider result for `int` inputs.

Sequences and selectors of `float` or `double` accumulate in `double` in
enumeration order. Float forms convert the final result to `float`.
For readonly or immutable floating element types, use a selector with the
matching protected parameter, such as `double Value(readonly(double) value)`
returning `(double)value`. Direct parameterless floating overloads currently
require unqualified elements.

Floating overflow produces infinity, and NaN propagates. Empty sums return
zero without invoking a selector. Accumulation starts at positive zero.

Each visited `Current` and selector are evaluated once. Selectors take their
input by value, following ordinary element copy/move semantics. Iterators
and temporary elements are destroyed on success or failure. Exceptions from
iteration or a selector propagate without visiting later elements. No sequence
buffer is allocated by the operation. All forms also have freestanding `Sum`
overloads.

## Qualified integer views

```csharp
using Standard;
using Standard::Memory;
using Standard::Queries;

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

The generic integer form also accepts `byte`, `sbyte`, `short`, `ushort`,
`char`, `uint`, and `nint`, including readonly and immutable views.
