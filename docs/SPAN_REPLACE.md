# Replacing span elements

`Replace` replaces elements equal to a selected value, either in place or while
copying into initialized destination storage.

```csharp
using Standard::Collections;
using Standard::Memory;

int main()
{
    int[4] data = [1, 2, 1, 3];
    Span<int> values = new(&data[0], 4);
    values.Replace(1, 9);
    if (data[0] != 9 || data[1] != 2 || data[2] != 9 || data[3] != 3) return 1;
    int[5] output = [0, 0, 0, 0, 99];
    Span<int> destination = new(&output[0], 5);
    values.AsImmutable().Replace(destination, 9, 7);
    if (output[0] != 7 || output[2] != 7 || output[4] != 99) return 2;
    return 0;
}
```

## Choosing equality

The default forms use `IEquatable<T>`. A final
`bool(immutable(T)*, immutable(T)*)*` callback allows another equality rule
without that interface. It receives the source element first and the old value
second. Elements are tested once in index order, even when the old and new values
are equal. An empty span invokes no callbacks.

Freestanding forms are `Replace(values, oldValue, newValue)` and
`Replace(source, destination, oldValue, newValue)`, optionally followed by the
callback. Include `Collections/span_replace.gf` for callback forms and
`Collections/default_equality.gf` plus `Collections/EqualityComparer.gf` for
default equality.

## Ownership and failure behavior

Old and new values are passed by value, snapshotting named or aliased arguments
before changing any elements. Elements must support copying. In-place replacement
copies the new value only into matches. The copy form also copies nonmatches to
the destination, leaving its unused tail untouched. Starting at the same address
acts like in-place replacement and leaves nonmatches untouched.

The destination must be at least as long as the source. Disjoint spans and spans
with the same starting address are supported. Any other overlap with the full
destination span is rejected before callbacks or element assignments. Argument
copies occur before entering the operation and can fail before these checks.

If equality or an element copy throws, completed assignments remain. A failed
copy preserves the current destination element, and unvisited elements remain
unchanged. Other callback side effects are observable. Keep the backing storage
valid and structurally unchanged during callbacks and copy constructors.

The operation allocates no temporary buffer. Argument and element copies can
allocate according to their types. Ordinary cleanup releases failed copies and
the argument snapshots. Pointer elements copy pointers, not pointees.
