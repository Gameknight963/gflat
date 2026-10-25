# Comparing lazy sequences

`SequenceEqual(other)` compares two `Enumerable` wrappers with the same element
type `T`, allowing their source and iterator types to differ. It returns true
when corresponding elements are equal and both sequences end together. Two
empty sequences are equal. An unequal pair or differing lengths returns false.

Default equality uses `EqualityComparer<T>::Equals` and requires the matching
closed `IEquatable<T>` contract. `SequenceEqual(other, equal)` instead accepts
`bool(immutable(T)*, immutable(T)*)*` and requires no equality-interface
implementation. The callback receives the left element first and right element
second. It can define another matching relation. Element qualifications follow
ordinary generic inference; readonly element views can use an explicit comparer
without assuming variance between differently constructed equality contracts.

```csharp
using std;
bool SameParity(immutable(int)* left, immutable(int)* right)
    => *left % 2 == *right % 2;
int main()
{
    int[3] first = [1, 2, 3];
    int[3] second = [5, 6, 7];
    Span<int> left = new(first, 3);
    Span<int> right = new(second, 3);
    if (!left.AsEnumerable().SequenceEqual(left.AsEnumerable())) return 1;
    if (left.AsEnumerable().SequenceEqual(right.AsEnumerable())) return 2;
    if (!left.AsEnumerable().SequenceEqual(right.AsEnumerable(), SameParity)) return 3;
    if (!left.AsImmutable().AsEnumerable().SequenceEqual(right.AsImmutable().AsEnumerable(), SameParity)) return 4;
    return 0;
}
```

## Evaluation and lifetime

The left iterator is constructed first, then the right iterator. Each comparison
round advances left once, then right once, including when left has just ended:
the right advancement determines whether both sequences ended together. When
only one advances successfully, the method returns false without reading either
terminal `Current` for that round. After an unequal pair it performs no further
advancement or element reads. No count, indexing, buffer-identity, or self-source
shortcut changes this order; even identical wrappers are enumerated independently.

When both advance, terminal `Current` is read once on the left, then once on the
right. Both values follow ordinary `T` semantics and are stored in local
temporaries before comparison. The comparer borrows those temporaries only for
the call and must not retain their addresses. Owning elements can therefore copy,
allocate, or throw. Pointer elements remain ordinary borrowed addresses. Upstream
query operators keep their own advancement and `Current` behavior; for example,
an upstream `Where` may read an element to test its predicate.

Constructed iterators and temporary owners are destroyed on exhaustion,
early return, and exceptions. Failure constructing the right iterator cleans up
the already-constructed left iterator. Failure reading or copying the right
element cleans up the left temporary. Original iterator/copy/comparer exceptions
propagate normally. Keep both backing sources alive and obey their borrowing
rules throughout the call. Callbacks must not invalidate either source through
another alias. The operation introduces no collection or GC allocation; source,
upstream, callback, and element-copy allocation behavior remains unchanged.

For contiguous spans, the existing
`SequenceEqual<T>(Span<immutable(T)>, Span<immutable(T)>, equal)` algorithm
borrows elements directly and can reject unequal lengths immediately. Query
sequence equality intentionally enumerates and follows ordinary element value
semantics.

Selective source users include `Queries/EnumerableSequenceEqual.gf`, plus
`Collections/EqualityComparer.gf` and `IEquatable.gf` for default equality, alongside their
normal source/enumeration dependencies. No new iterator type or compiler feature
is required.
