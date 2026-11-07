# Transforming an aggregate result

`Aggregate(seed, accumulate, resultSelector)` folds a sequence and transforms the
final accumulator into the result. The accumulator and result can have different
types.

```csharp
using Standard::Memory;
using Standard::Queries;

struct Totals
{
    public long Sum;
    public long Count;
    public Totals() { Sum = 0; Count = 0; }
}

Totals Add(Totals totals, int value)
{
    totals.Sum += (long)value;
    totals.Count++;
    return totals;
}

double Mean(Totals totals)
{
    if (totals.Count == 0) return 0.0;
    return (double)totals.Sum / (double)totals.Count;
}

int main()
{
    int[3] data = [10, 20, 30];
    Span<int> values = new(&data[0], 3);
    double mean = values.AsEnumerable().Aggregate(new Totals(), Add, Mean);
    if (mean != 20.0) return 1;
    return 0;
}
```

The accumulator callback runs once per element in enumeration order. The result
selector runs once after the iterator is destroyed. Empty sequences still pass
the seed to the selector. An enumeration or accumulator failure prevents the
selector from running.

## Owning accumulators and results

Both callbacks receive the accumulator by value, transferring ownership from the
fold. An owning accumulator callback should return `move accumulator` to reuse
it. Move a named seed into `Aggregate` to avoid copying it. The result selector
can consume that accumulator and return a different owning result, including a
noncopyable type. A named owning result must also be returned with `move`.

If iteration or either callback throws, ordinary cleanup releases the current
accumulator, iterator, and any callback locals. A selector that throws cannot
replace the caller's existing assignment destination. Callback side effects
remain observable. Keep the sequence's borrowed storage valid during enumeration.

The operation uses `Queries/enumerable_aggregate.gf`. The freestanding form is
`Aggregate(source, seed, accumulate, resultSelector)`. Type arguments can be
inferred for both forms. See [query aggregation](QUERIES.md#aggregate) for the
seeded and unseeded forms without a result selector.
