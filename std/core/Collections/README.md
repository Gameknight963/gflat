# Enumeration and queries

`Vector`, `Span`, and `IntRange` provide an `AsEnumerable()` view for lazy queries.
Import `std` to make the query extensions visible:

```csharp
using std;

int main()
{
    Vector<int> numbers = new Vector<int>();
    for (int i = 1; i <= 10; i++) numbers.Add(i);

    Vector<int>* borrowed = &numbers;
    var query = borrowed.AsEnumerable()
        .Where((int n) => n % 2 == 0)
        .Select((int n) => n * n)
        .Take(3);

    foreach (int number in query)
    {
        String line = s$"{number}";
        Console::WriteLine(ref line);
    }
    return 0;
}
```

The query stores a span of the vector's elements. It copies neither the vector
nor its elements when the query is built. Keep the vector alive and do not grow,
clear, remove from, or move it while using the view. The length is captured when
the view is created. Editing existing elements is visible on the next read.
For a readonly view use `numbers.AsReadOnly().AsEnumerable()`.
Its callback parameter types retain that qualification, for example
`.Where((readonly(int) n) => n > 0)`.

## Operations

| Operation | Behavior |
| --- | --- |
| `Where(predicate)` | Yield elements passing the predicate. |
| `Select(selector)` | Project elements when their `Current` value is read. |
| `Skip(count)` / `Take(count)` | Skip or yield at most `count` elements; counts are `nuint`. |
| `Any()` | Test for at least one element, stopping immediately. |
| `All(predicate)` | Stop at the first failure; true for an empty sequence. |
| `Count()` | Count elements without reading `Current`; throw on count overflow. |
| `First()` / `Last()` / `Single()` | Return a value; throw if empty. `Single` also rejects multiple elements. |
| `TryFirst(ref result)` | Return false without changing `result` if empty. |
| `Aggregate(seed, function)` | Fold values into an accumulator, returning the seed if empty. |
| `ForEach(action)` | Visit every element. |
| `ToVector()` | Copy values into a new owning vector. |

Operators are lazy until a terminal operation or `foreach` advances an iterator.
Each enumeration starts a fresh iterator. Operator order matters:
`Take(3).Where(test)` examines at most three input elements;
`Where(test).Take(3)` looks for three matches.
`Take(0)` never advances its input.

`Select` does not cache projected values. Reading its `Current` twice invokes
the selector twice. `Any` and `Count` need not invoke a selector at all. This
allows arbitrary result types without default construction or hidden storage
allocation. Put effects in `ForEach`, rather than relying on a selector running.

The built-in source views and operators do not allocate. A callback, a custom
source or element's copy constructor, or materializing a vector can allocate.
Callback signatures use ordinary
function pointers; capturing closures are not implemented.

## Ownership

Elements follow ordinary value semantics. Reading a `String` element copies it;
`ToVector()` creates independent owning strings. Reading a pointer copies its
address. The query and the resulting vector do **not** delete its pointee.
Managed pointer elements keep their normal collector behavior.

Iterators are values. Their destructors and owned fields are cleaned up on
completion, early return, `break`, and exceptions. A custom iterator returned as
a raw pointer has ordinary borrowed-pointer semantics; returning a pointer does
not make the query responsible for deleting it.

## Custom sequences and optional interfaces

`IEnumerable<T, Iterator>` requires `GetEnumerator()` to return the specified
concrete iterator type. Keeping this second type parameter avoids requiring a
heap-allocated interface iterator. The value-producing `IEnumerator<T>` contract
requires `bool MoveNext()` and a `T Current` getter. Borrowing iterators, such as
`Span<T>::Enumerator`, can expose `ref Current` and use the pattern directly.

`foreach` continues to accept the pattern without either interface.
For custom queries, construct `Enumerable<T, Source, Iterator>` with a source
whose `GetEnumerator()` creates an iterator. The source must be copyable: it is
copied into composed views. Usually it should contain borrowed pointers or
other small views; it must not retain a pointer to a temporary source object.
Iterators themselves may own resources and need not be copyable.
