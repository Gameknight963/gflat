# Searching lazy sequences

`Any(predicate)` tests a sequence in order and returns true at the first element
passing the predicate. Empty sequences return false without invoking it.
Parameterless `Any()` retains its existing behavior: it only advances once and
does not read the terminal iterator's `Current`.

`Contains(immutable(T)* key)` searches with `EqualityComparer<T>::Equals` and
requires `T : IEquatable<T>`. The key is borrowed rather than copied. The overload
`Contains(key, equal)` accepts a callback of type
`bool(immutable(T)*, immutable(T)*)*` and imposes no equality-interface constraint.
The first callback argument is the sequence element; the second is the search
key. Both overloads return true at the first match, or false when exhausted.

Default equality requires the matching closed `IEquatable<T>` contract for the
sequence's actual element type. For example, a readonly `int` element view does
not automatically use `IEquatable<int>` through interface variance. Use the
explicit comparer overload for such views; it needs no equality-interface
implementation.

```csharp
using std;
bool Even(int value) => value % 2 == 0;
bool SameParity(immutable(int)* left, immutable(int)* right)
    => *left % 2 == *right % 2;
bool Same(immutable(int)* left, immutable(int)* right) => *left == *right;
int main()
{
    int[4] values = [1, 2, 3, 4];
    Span<int> view = new(values, 4);
    var query = view.AsEnumerable();
    if (!query.Any(Even)) return 1;
    int key = 3;
    if (!query.Contains(&key)) return 2;
    int equivalent = 5;
    if (!query.Contains(&equivalent, SameParity)) return 3;
    if (!view.AsImmutable().AsEnumerable().Contains(&key, Same)) return 4;
    return 0;
}
```

These operations start a fresh iterator and short-circuit. They read terminal
`Current` once per tested element. Upstream operators retain their usual rules:
for example, `Where` may read its own source to evaluate its predicate before
the terminal reads an accepted value. An early match does not validate a
malformed suffix of a lazy Unicode source.

Elements follow ordinary `T` value semantics. Searching a borrowed sequence of
owning values can copy each tested element and can therefore allocate or throw.
The explicit comparer borrows a local materialized element, so it must not retain
that pointer beyond the callback. The key must stay readable throughout the
operation. Callbacks must not mutate, move, destroy, or invalidate the sequence
through another alias. Pointer elements remain ordinary borrowed addresses.

Successfully constructed iterators and temporary element owners are destroyed
on exhaustion, early success, and exceptions. Iterator construction/advancement,
`Current`, element copying, equality, and predicates can throw; exceptions
propagate normally. The terminal methods introduce no collection or GC
allocation. Allocation behavior of custom sources, callbacks, element copies,
and allocating upstream operators remains unchanged.

Default equality is the existing `IEquatable<T>` contract. The supplied comparer
can express another matching relation without changing stored values. For
contiguous spans, the existing `Contains<T>(Span<immutable(T)>, key, equal)`
algorithm borrows elements directly; query searching intentionally follows the
normal value semantics of sequence enumeration.

Selective source users need `Collections/EnumerableExtensions.gf` for `Any`,
and `Collections/EnumerableContains.gf` plus `EqualityComparer.gf` and
`IEquatable.gf` for default query membership, alongside the sequence's ordinary
source and enumeration dependencies. Explicit-comparer calls do not require an
`IEquatable<T>` implementation.
