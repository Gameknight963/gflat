# Reversing a lazy sequence

`Reverse()` returns a repeatable query yielding the input elements in reverse
order. It leaves the caller's source order unchanged. Each enumerator owns an
independent `Vector<T>` buffer; this differs from the existing span `Reverse`
algorithm, which changes a mutable span in place.

```csharp
using std;
int main()
{
    int[4] values = [1, 2, 3, 4];
    Span<int> source = new(values, 4);
    var backwards = source.AsEnumerable().Reverse();
    int expected = 4;
    foreach (int value in backwards)
    {
        if (value != expected) return 1;
        expected--;
    }
    if (expected != 0 || values[0] != 1 || values[3] != 4) return 2;
    return 0;
}
```

Creating the query or its enumerator does not enumerate the underlying source
or allocate the element buffer. The first `MoveNext()` materializes the entire
input with existing `ToVector()`, then positions at the final buffered element.
Even `Reverse().Take(1)` must read the whole input before yielding a value.
`Reverse().Take(0)` never starts materialization. A source that does not end
cannot produce its first reversed element.

Source views still follow normal value-copy rules. Built-in borrowed views copy
only their small metadata. Copy constructors on custom source values can have
their own allocation or exception effects when wrappers or iterators are copied;
deferring enumeration does not suppress those ordinary semantics.

Input changes before the first move are visible during materialization. After
buffering, the iterator reads its buffered values; another enumeration creates
a new buffer from the then-current source. Keep the backing source alive and
obey its borrowing rules while the query can still enumerate it. Ordinary
`T` semantics apply: owning values may be copied and allocate, while pointer
and managed-reference elements retain their normal shared targets.

The buffered element type must be accepted by `Vector<T>`. Its current writable
indexer prevents storing an element type with outer `readonly` or `immutable`
qualification, even though reversing the query does not swap stored elements.
For a readonly integer view, explicitly project each value to `int` before
buffering:

```csharp
using std;
int main()
{
    int[3] values = [1, 2, 3];
    Span<int> source = new(values, 3);
    var backwards = source.AsReadOnly().AsEnumerable()
        .Select((readonly(int) value) => (int)value).Reverse();
    int expected = 3;
    foreach (int value in backwards)
    {
        if (value != expected) return 1;
        expected--;
    }
    return expected;
}
```

This projection chooses the stored value type explicitly; `Reverse()` does not
remove qualification automatically. `ToVector()` has the same storage restriction.

`Current` returns an ordinary `T` value, copying buffered owners when required.
It throws before the first successful move, after exhaustion, or after failed
materialization. Repeated reads refer to the same buffered position. A `Current`
copy failure propagates without advancing that position.

If initial source construction, enumeration, copying, or allocation fails, the
original exception propagates and temporary materialization storage is cleaned
up. That iterator remains finished: later moves return false without retrying
the source, and `Current` stays invalid. A new enumeration can try again.
Successfully buffered storage is retained until the iterator is destroyed,
including after exhaustion, and is released on early loop exit or exceptions
through normal ownership cleanup. Reverse adds no GC allocation of its own.

The operation uses O(n) element storage and materialization work before its first
result. Later moves are constant-time; element copying retains its usual costs.
Selective source users include `Queries/EnumerableReverse.gf` and
`Queries/ReverseSource.gf`, along with `EnumerableExtensions.gf` for
`ToVector`, the normal enumeration contracts, and `Vector<T>` dependencies.
