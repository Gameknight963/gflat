# Copy values from either end of a deque

`Deque<T>` provides optional `PeekFirst()`, `PeekLast()`,
`TryPeekFirst(ref output)`, and `TryPeekLast(ref output)` extensions in
`Standard::Collections`. These copy an element without removing it.

```csharp
using Standard::Collections;
using Standard::Text;

int main()
{
    Deque<String> items = new();
    String output = s"previous";
    if (items.TryPeekFirst(ref output) || !output.Equals("previous")) return 1;
    items.AddLast(s"first");
    items.AddLast(s"last");
    String first = items.PeekFirst();
    if (!items.TryPeekLast(ref output) || !output.Equals("last")) return 2;
    items.Clear();
    if (!first.Equals("first") || !output.Equals("last")) return 3;
    return 0;
}
```

`T` must be copyable. An owning value such as `String` gets independent
storage. Use the borrowed `Front` and `Back` properties for noncopyable values
or when a copy is unnecessary.

An empty `PeekFirst` or `PeekLast` throws. An empty try-peek returns false and
leaves output unchanged without copying or allocating. Otherwise the copy is
constructed before replacing output. Copying or allocation failure preserves
output and the deque. Output can refer to a stored element, including the
source element itself. Successful replacement updates that slot.

Selective builds include `Collections/Deque.gf` and
`Collections/deque_peek.gf`. Freestanding forms take an immutable deque pointer
first, for example `PeekFirst(&items)` and `TryPeekLast(&items, ref output)`.
The optional helpers do not prevent borrowed operations on noncopyable elements.
