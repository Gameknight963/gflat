# Removing a specific queued value

`PriorityQueue<T>.Remove(value, ref output)` searches for an equal value and
transfers the stored value into initialized output. It returns false when no
match exists, leaving output unchanged. Priority ordering and value equality
are separate: items can compare equal in priority without being equal values.

```csharp
using Standard::Collections;
using Standard::Text;

int Compare(immutable(String)* left, immutable(String)* right) => left.CompareTo(right);

int main()
{
    PriorityQueue<String> pending = new(Compare);
    pending.Enqueue(s"second");
    pending.Enqueue(s"first");
    String requested = s"second";
    String removed = s"previous";
    if (!pending.Remove(&requested, ref removed)) return 1;
    if (!removed.Equals("second") || pending.Count != 1 || !pending.Top.Equals("first")) return 2;

    requested = s"missing";
    if (pending.Remove(&requested, ref removed) || !removed.Equals("second")) return 3;
    return 0;
}
```

## Equality and ownership

The default overload requires `T : IEquatable<T>`. To choose equality explicitly,
pass a `bool(immutable(T)*, immutable(T)*)*` callback as the final argument.
The freestanding forms take `ref queue` first, for example
`Remove(ref pending, &requested, ref removed)`.

If several stored values match, one matching value is removed. The search stops
at its first match in heap storage, whose order is not a priority-sorted order.
Do not rely on insertion order or the lowest priority among matching values.
The returned value is the actual stored owner, which may contain different
payload or priority data than the lookup value. No element copies are required.

The lookup value is borrowed and can refer to a stored element. Output may refer
to the lookup variable itself, but it must be outside the queue's entire backing
allocation, including spare capacity. A nonempty queue rejects overlapping output
before invoking callbacks. A successful removal destroys the previous output
value and can relocate other queued elements. Reacquire borrowed item references
and iterators afterward.

## Cost and errors

Searching takes O(n) equality calls. Heap repair takes O(log n) priority
comparisons and relocations. Removal does not allocate or shrink queue storage.
An absent value makes no priority comparisons, and an empty queue invokes neither
callback. Callback code can still allocate or throw.

Equality checks and heap-repair comparisons finish before values move. If either
callback throws, output, queue contents, count, capacity, and stored element
addresses remain unchanged. Callbacks must leave the queue and its values
unchanged and preserve consistent priority ordering.

Selective builds load `Collections/PriorityQueue.gf` and
`Collections/priority_queue_remove.gf` with their Vector and Span dependencies.
The default equality overload also needs `Collections/EqualityComparer.gf` and
the element's equality implementation.
