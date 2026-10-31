# Sum integer query values

`Sum()` returns a checked `long` total. Empty input returns `0l`.

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

Elements must implicitly convert to `long`. Supported built-in types are
`byte`, `sbyte`, `short`, `ushort`, `char`, `int`, `uint`, `long`, and `nint`,
including readonly and immutable views. Other types need an explicit Select
projection or a different accumulator.

Every addition is checked in source order. An overflowing prefix throws
`Exception("Sequence sum overflow")`, even if a later value would cancel it.
For example, `[9223372036854775807l, 1l, -1l]` fails at its second element,
while `[2147483647, 1]` produces `2147483648l`.

The scan reads each `Current` once and stops on the first input or overflow
error. Use `Aggregate` for another accumulator type or arithmetic policy.

Source: `Queries/enumerable_sum.gf`.
