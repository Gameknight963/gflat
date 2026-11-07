# Converting every vector element

`Vector<T>.ConvertAll<U>(converter)` builds a new owning `Vector<U>` in source
order. The converter borrows an immutable pointer to each input element and
returns the corresponding output value.

```csharp
using Standard::Collections;

long Square(immutable(int)* value) => (long)(*value) * (long)(*value);

int main()
{
    Vector<int> values = new();
    values.Add(3);
    values.Add(7);
    Vector<long> squares = values.ConvertAll<long>(Square);
    if (squares.Count != 2 || squares[0] != 9l || squares[1] != 49l) return 1;
    if (values[0] != 3 || values[1] != 7) return 2;
    return 0;
}
```

Storage is reserved before the first conversion. Each input is visited once,
from index zero upward. An empty vector returns an empty result without
allocating or calling the converter. Allocation failure occurs before callbacks.

Inputs are borrowed and returned values are moved into the result. Neither
input nor output elements need a copy constructor. The source may be immutable.
The result owns its storage and any owning values returned by the converter.
Pointer results remain pointers and do not gain ownership of their pointees.

If the converter throws, completed output values are destroyed and the partial
vector is released. The source and an assignment destination stay unchanged.
Callback side effects remain observable. Keep source storage stable throughout
conversion and do not mutate it or its elements from the converter.

Selective builds add `Collections/vector_convert_all.gf`. A freestanding form
is available as `ConvertAll<T, U>(sourcePointer, converter)`. For lazy conversion,
use the query `Select` operation instead.
