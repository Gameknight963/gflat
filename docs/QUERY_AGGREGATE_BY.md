# Aggregate values by key

`AggregateBy` consumes a query and returns an owning `HashMap<K, U>` containing
one accumulator per selected key. It does not retain a collection of input
elements for each group. Map iteration order is unspecified.

| Form | Initial accumulator for a new key |
| --- | --- |
| `source.AggregateBy(selector, seed, accumulate)` | A copy of `seed` |
| `source.AggregateBy(selector, seedSelector, accumulate)` | The result of `seedSelector(key)` |

The callback types are `K(T)*` for the key selector, `U(K)*` for the seed factory,
and `U(U, T)*` for the reducer. The reducer also processes the first element of
each group. Existing groups continue from their previous result in encounter
order. The seed factory runs once per distinct key, using its first selected
representative.

```csharp
using Standard::Collections;
using Standard::Memory;
using Standard::Queries;

struct Sale
{
    public int Department;
    public int Amount;
}

int Department(Sale value) => value.Department;
long Add(long total, Sale value) => total + (long)value.Amount;
long OpeningBalance(int department) => (long)department * 100l;

int main()
{
    Sale[3] sales = default(Sale[3]);
    sales[0].Department = 1; sales[0].Amount = 20;
    sales[1].Department = 2; sales[1].Amount = 7;
    sales[2].Department = 1; sales[2].Amount = 5;
    Span<Sale> input = new(sales, 3);
    HashMap<int, long> totals = input.AsEnumerable().AggregateBy(Department, 0l, Add);
    HashMap<int, long> balances = input.AsEnumerable().AggregateBy(Department, OpeningBalance, Add);
    int first = 1;
    int second = 2;
    if (totals.Count != 2 || totals[&first] != 25l || totals[&second] != 7l) return 1;
    if (balances[&first] != 125l || balances[&second] != 207l) return 2;
    return 0;
}
```

Freestanding forms use the same argument order, for example
`AggregateBy(source, selector, seed, accumulate)`.

## Evaluation and ownership

The call consumes the complete input before returning. For each element it
reads `Current` once, calls the key selector once, and calls the reducer once.
Empty input returns an empty map without invoking callbacks or allocating map
storage. Preparing by-value arguments and opening a custom iterator can still
have their own costs and effects.

Keys must implement `IHashable` and `IEquatable<K>`. Equal keys share one
accumulator, with the first selected key retained. Retained keys must keep
stable hashing and equality. Keys and accumulators must own their data or
borrow storage that outlives the result, not temporary callback arguments.

Accumulators must be copyable. The reducer receives a copy of an existing
accumulator, then its returned owner replaces that stored value. The fixed
seed is copied for each new group. The seed-factory form also requires copyable
keys because its callback receives a key by value. These copies follow the
types' ordinary copy semantics.

Iterator, callback, copy, hash, equality, and allocation errors propagate. A
failed call destroys its partial map, iterator, and completed temporary owners.
Consumed input and callback side effects remain visible.

The extension source is `Queries/enumerable_aggregate_by.gf`.
