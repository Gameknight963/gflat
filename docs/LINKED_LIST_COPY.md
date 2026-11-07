# Copying linked-list elements

Load `Collections/linked_list_copy.gf` for `LinkedList<T>.CopyTo(destination)`,
`CopyTo(destination, destinationIndex)`, and `TryCopyTo(destination)`. Destinations
are initialized `Span<T>` elements, and values are copied from first to last.

```csharp
using Standard::Collections;
using Standard::Memory;

int main()
{
    LinkedList<int> values = new();
    values.AddLast(10);
    values.AddLast(20);
    int[4] buffer = [-1, -1, -1, -1];
    Span<int> output = new(&buffer[0], 4);
    values.CopyTo(output, 1);
    if (buffer[0] != -1 || buffer[1] != 10 || buffer[2] != 20 || buffer[3] != -1) return 1;
    if (values.TryCopyTo(output.Slice(3))) return 2;
    return 0;
}
```

A short destination makes `TryCopyTo` return false without writing. `CopyTo`
throws for a short destination or invalid destination index. The written prefix
must not overlap any node allocation or the list's internal state. All overlap
checks finish before the first copy.

These operations allocate no container storage. Element copies may allocate or
throw. A throwing copy leaves the completed destination prefix replaced and the
failing slot unchanged. The list retains its values and node identities. Keep it
alive and structurally unchanged while copying. Copyability is required only when
these extensions are called.
