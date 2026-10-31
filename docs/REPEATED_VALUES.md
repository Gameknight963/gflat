# Repeated values

`Standard::Queries::Repeated<T>` captures one value and presents it a specified
number of times:

```csharp
using Standard;
using Standard::Queries;

int main()
{
    Repeated<int> values = new(7, 4);
    if (values.Count != 4 || values.IsEmpty) return 1;
    int sum = 0;
    foreach (int value in values) sum += value;
    if (sum != 28) return 2;
    Repeated<int>::Enumerator cursor = values.GetEnumerator();
    if (!cursor.MoveNext() || cursor.Current != 7) return 3;
    if (values.AsEnumerable().Take(2).Count() != 2) return 4;
    return 0;
}
```

`Count` and `IsEmpty` read the captured count. Each enumerator owns a copy of the
captured value. Reading Current copies that value again. T therefore needs a
copy operation. Strings produce independent contents, while pointer values share
their pointees.

## Ownership

Destroying or replacing the sequence leaves existing cursors usable. Each cursor
owns its captured value until destruction, even after exhaustion. A zero-count
sequence still owns its capture and copies it when creating an iterator.

`AsEnumerable()` also copies the captured source. Even Take(0) can therefore copy
or allocate. Use the Count property for constant-time count inspection. Query
Count enumerates normally.

Source: `core/Queries/Repeated.gf`
