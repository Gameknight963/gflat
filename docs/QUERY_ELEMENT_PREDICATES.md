# Selecting an element with a predicate

`First(predicate)`, `Last(predicate)`, and `Single(predicate)` select matching
elements directly from a query.

```csharp
using Standard::Memory;
using Standard::Queries;

bool Positive(int value) => value > 0;
bool AboveFive(int value) => value > 5;

int main()
{
    int[4] data = [-2, 3, 0, 7];
    Span<int> values = new(&data[0], 4);
    if (values.AsEnumerable().First(Positive) != 3) return 1;
    if (values.AsEnumerable().Last(Positive) != 7) return 2;
    if (values.AsEnumerable().Single(AboveFive) != 7) return 3;
    int result = 99;
    if (values.AsEnumerable().TrySingle(Positive, ref result) || result != 99) return 4;
    return 0;
}
```

## Results and enumeration

| Operation | Result |
| --- | --- |
| `First(predicate)` | First match, stopping immediately |
| `Last(predicate)` | Last match after reaching the end |
| `Single(predicate)` | Only match, throwing when a second match is found |
| `TryFirst(predicate, ref result)` | False for no match |
| `TryLast(predicate, ref result)` | False for no match |
| `TrySingle(predicate, ref result)` | False for no match or multiple matches |

The throwing forms use `Exception("Sequence contains no matching element")`
when no element matches, including empty input. A second match makes `Single`
throw `Exception("Sequence contains more than one matching element")`.
`Single` and `TrySingle` stop at the second match rather than inspecting the
remaining sequence.

Each visited element is read from `Current` exactly once and passed to the
predicate once. Predicates run in enumeration order. The selected value is the
retained candidate, not another read of `Current`. This matters for iterators
whose getters produce fresh values or have side effects.

## Ownership and failures

Predicates have type `bool(T)*` and receive element values by value. These
overloads require copyable element values so a candidate can be retained during
its predicate call. Sequences of borrowed pointers also support noncopyable
pointees, using the pointer-valued predicate type supplied by that sequence.

Unselected candidates and predicate argument copies are destroyed normally.
The selected candidate transfers to the caller or ref output. `Try...` forms
replace output only after determining a successful result. False returns and
iterator, predicate, or element-copy failures leave output unchanged. Keep output
separate from storage changed by iteration or callbacks. Callback side effects
remain observable, and borrowed sequence storage must stay valid throughout.

Use `Queries/enumerable_element_predicates.gf` with explicit source lists. The
freestanding forms take the query first, such as `First(source, predicate)` and
`TryLast(source, predicate, ref result)`. No `WhereSource` dependency is needed.
