# Copying matching vector elements

`Vector<T>.FindAll(predicate)` returns a new owning vector of matching elements,
preserving their order. The predicate borrows an immutable pointer to each input.

```csharp
using Standard::Collections;

bool Even(immutable(int)* value) => *value % 2 == 0;

int main()
{
    Vector<int> values = new();
    values.Add(1);
    values.Add(2);
    values.Add(4);
    Vector<int> matches = values.FindAll(Even);
    if (matches.Count != 2 || matches[0] != 2 || matches[1] != 4) return 1;
    matches[0] = 99;
    if (values[1] != 2) return 2;
    return 0;
}
```

On success, the predicate is called once per element, from index zero upward.
Only matching elements are copied. Result storage grows as matches are found.
Empty inputs and inputs without matches produce an empty vector without result
allocation or element copies. A predicate may still allocate on its own.

The result has independent storage. Pointer elements remain pointers and do not
gain ownership of their pointees. This operation requires copyable elements.
It lives in `Collections/vector_find_all.gf`, keeping that requirement separate
from borrowed searches over noncopyable values. The source may be immutable.

Predicate, copy, and allocation failures stop the operation. Completed result
values and any temporary copy are destroyed, and partial storage is released.
The source and an assignment destination stay unchanged. Callback and copy
constructor side effects remain observable. Keep source storage stable and do
not mutate it from either callback.

A freestanding form is `FindAll<T>(sourcePointer, predicate)`. For a borrowed
single match use [Find](VECTOR_PREDICATES.md). For lazy filtering use `Where`.
