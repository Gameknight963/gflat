# Searching vectors with a predicate

`Vector<T>` provides `Find`, `FindLast`, `FindIndex`, `FindLastIndex`, `Exists`,
and `TrueForAll`. Predicates borrow an immutable pointer to each examined element.

```csharp
using Standard::Collections;

bool Positive(immutable(int)* value) => *value > 0;

int main()
{
    Vector<int> values = new();
    values.Add(-3);
    values.Add(7);
    values.Add(9);
    if (values.FindIndex(Positive) != 1) return 1;
    immutable(int)*? found = values.FindLast(Positive);
    if (found == null || *found! != 9) return 2;
    if (!values.Exists(Positive) || values.TrueForAll(Positive)) return 3;
    return 0;
}
```

`Find` and `FindLast` return borrowed pointers into the vector, or null when no
element matches. Keep the vector alive and reacquire the pointer after structural
mutation or storage relocation. Pointer results observe changes through aliases.
No element is copied, so noncopyable owners are supported. The source may be immutable.

Index searches return `Count` when no match exists. Forward searches visit indices
from zero upward. Last searches visit from `Count - 1` downward. Each stops at the
first match in its direction. `Exists` uses the forward search. `TrueForAll` visits
from zero upward and stops at the first false predicate result.

Empty vectors return null from value searches, zero from index searches, false
from `Exists`, and true from `TrueForAll`, without invoking the predicate.
These linear scans allocate no storage. Predicate exceptions propagate before
assigning a result. Do not mutate the source or relocate its storage from a predicate.

Selective builds add `Collections/vector_predicates.gf`. Each method also has
a freestanding form with an immutable vector pointer as its first argument.
