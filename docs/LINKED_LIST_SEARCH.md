# Search linked-list values

`LinkedList<T>` has optional search extensions in `Standard::Collections`.
They compare borrowed values and do not require copyable elements.

| Method | Result |
| --- | --- |
| `Find(&key)` | First matching node, or null |
| `FindLast(&key)` | Last matching node, or null |
| `Contains(&key)` | Whether a matching value exists |
| `RemoveValue(&key)` | Destroy the first matching value and node, returning false when absent |

The overloads above use `IEquatable<T>`. Each also accepts an explicit
`bool(immutable(T)*, immutable(T)*)*` equality callback as its final argument.
The callback receives the stored value first and the query key second.

```csharp
using Standard::Collections;

int main()
{
    LinkedList<int> values = new();
    values.AddLast(2);
    values.AddLast(1);
    values.AddLast(2);
    int key = 2;
    LinkedList<int>::Node*? first = values.Find(&key);
    LinkedList<int>::Node*? last = values.FindLast(&key);
    if (first == null || last == null || first == last) return 1;
    if (!values.RemoveValue(&key) || values.Count != 2) return 2;
    if (values.Find(&key) != last) return 3;
    int removed = values.Remove(last!);
    if (removed != 2 || values.Contains(&key)) return 4;
    return 0;
}
```

`Remove(node)` transfers the removed element to the caller. `RemoveValue(&key)`
destroys the element instead. Use the node-based operation after `Find` when
the removed owner should be retained.

## Search order and ownership

`Find`, `Contains`, and `RemoveValue` search from first to last and stop at the
first match. `FindLast` searches backward from the last node. Work is linear
in the number of visited nodes, with constant auxiliary storage.

Searches do not allocate, copy elements, or change links. The equality callback
can have its own allocation and side effects. Empty searches return immediately
without calling it. `Contains` also supports immutable list receivers.

Returned node pointers borrow the list's storage. They remain valid through
insertion, reversal, and owner moves. Removing their node or destroying the
responsible list ends their lifetime. Keep the owner alive while using them.

The query key may borrow a stored value. Lookup finishes before `RemoveValue`
unlinks anything, and the key is not read afterward. A key borrowed from the
removed node is invalid once the method returns.

An equality exception leaves the list and its nodes unchanged. Callbacks must
leave list structure and compared values unchanged. A missing removal returns
false without destroying anything.

## Source selection

Selective builds include `Collections/linked_list_search.gf` alongside
`Collections/LinkedList.gf`. Default equality additionally uses
`Collections/EqualityComparer.gf` and the element's `IEquatable<T>` implementation.
Explicit callbacks support types without that interface.

Freestanding forms take a list pointer first, for example
`Find(&values, &key)` and `RemoveValue(&values, &key, equal)`.
