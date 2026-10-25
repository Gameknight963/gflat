# Value and span algorithms

`Swap` borrows two initialized values. The collection algorithms borrow a span
and never copy or own the source collection.
Import `std::Collections` and call them as ordinary functions:

```csharp
using std; using std::Collections;

int Compare(readonly(int)* left, readonly(int)* right)
{
    if (*left < *right) return -1;
    if (*left > *right) return 1;
    return 0;
}

int main()
{
    int first = 9;
    int second = 2;
    Swap(ref first, ref second);
    Vector<int> values = new();
    values.Add(second); values.Add(first); values.Add(5);
    Sort<int>(values.AsSpan(), Compare);
    int key = 5;
    nuint index = LowerBound<int>(values.AsImmutable(), &key, Compare);
    return (int)index - 1;
}
```

| Function | Behavior |
| --- | --- |
| `Swap<T>(ref T left, ref T right)` | Exchange two initialized mutable values by relocation; the same storage is a no-op. |
| `Sort<T>(Span<T>, compare)` | In-place ascending heapsort; O(n log n) comparisons and constant extra storage. Not stable. |
| `NthElement<T>(Span<T>, nth, compare)` | Select a rank and partition around it without sorting either side. Optional `Collections/nth_element.gf`; see [rank selection](../../../docs/NTH_ELEMENT.md). |
| `StableSort<T>(Span<T>, compare)` | Stable ordering with O(n log n) comparisons and two allocated index buffers. Optional `Collections/stable_sort.gf`; see [ownership and failure guarantees](OWNING_COLLECTIONS.md#stable-sorting). |
| `NextPermutation<T>(Span<T>, compare)` | Advance lexicographically, or wrap to the first ordering and return false. Optional `Collections/span_permutations.gf`; see [arrangements](../../../docs/PERMUTATIONS.md). |
| `PreviousPermutation<T>(Span<T>, compare)` | Move to the previous ordering, or wrap to the last ordering and return false. Same optional source. |
| `Reverse<T>(Span<T>)` | Reverse elements in place without invoking user code. |
| `Rotate<T>(Span<T>, middle)` | Move the prefix before `middle` after the suffix, preserving order within each part. |
| `LowerBound<T>(Span<immutable(T)>, key, compare)` | First index whose element compares greater than or equal to the borrowed key. |
| `UpperBound<T>(Span<immutable(T)>, key, compare)` | First index whose element compares greater than the borrowed key. |
| `BinarySearch<T>(Span<immutable(T)>, key, compare)` | First matching index in sorted input, or span length when absent. |
| `IsSorted<T>(Span<immutable(T)>, compare)` | Test whether every adjacent pair is in ascending order; equal elements are allowed. |
| `SequenceEqual<T>(first, second, equal)` | Compare equal-length `Span<immutable(T)>` views element by element, stopping at the first difference. Optional `IEquatable<T>` default in `default_equality.gf`. |
| `SequenceCompare<T>(first, second, compare)` | Lexicographically compare `Span<immutable(T)>` views, preserving the first nonzero comparator result; an equal prefix sorts by length. Optional `IComparable<T>` default in `default_ordering.gf`. |
| `MinIndex<T>(Span<immutable(T)>, compare)` | Index of the first least element, or span length when empty. Optional `IComparable<T>` default in `default_ordering.gf`. |
| `MaxIndex<T>(Span<immutable(T)>, compare)` | Index of the first greatest element, or span length when empty. Optional `IComparable<T>` default in `default_ordering.gf`. |
| `FindIndex<T>(Span<immutable(T)>, predicate)` | First matching index, or span length when no element matches. |
| `FindLastIndex<T>(Span<immutable(T)>, predicate)` | Last matching index, or span length when no element matches. |
| `IndexOf<T>(Span<immutable(T)>, key, equal)` | First equal element's index, or span length when absent. Optional `IEquatable<T>` default in `default_equality.gf`. |
| `LastIndexOf<T>(Span<immutable(T)>, key, equal)` | Last equal element's index, or span length when absent. Optional `IEquatable<T>` default in `default_equality.gf`. |
| `Contains<T>(Span<immutable(T)>, key, equal)` | Whether any element equals the borrowed key. Optional `IEquatable<T>` default in `default_equality.gf`. |
| `Partition<T>(Span<T>, predicate)` | Move matching elements into a prefix and return its length; relative order is not preserved. |
| `Shuffle<T>(Span<T>, ref PseudoRandom)` | Randomly permute elements in place using the supplied generator. |

`Swap` supports ordinary values and noncopyable owners such as `Unique<T>`.
It copies no owned buffers, invokes no user copy constructor or destructor, and
does not allocate for ordinary values. Managed references use the existing root
tracking operations. Both values remain initialized after the call. The two
arguments can be the same storage or distinct fields/elements; partially
overlapping storage created through unsafe aliases is unsupported. Ref arguments
are evaluated before the function starts, so a failing argument expression does
not begin a swap.

The storage addresses do not change. Pointers to the value slots still point to
those slots and now observe exchanged contents. Pointers into separately owned
buffers or pointees keep their addresses, with lifetime responsibility now held
by the opposite owner. Raw aliases and pointers stored inside values are not
retargeted. Keep both argument locations valid throughout the operation.
Readonly/immutable values, temporaries, ordinary value properties, uninitialized
storage, and fields inside managed objects cannot be passed as mutable refs under
the existing language rules. Native managed-reference slots can be swapped.

Both bounds return the span length if no such element exists, including zero
for an empty span. Input must already be sorted under the same comparator.
Their result identifies the insertion position, whether or not the key exists.
For duplicate keys, `[LowerBound, UpperBound)` describes the matching range.

Comparators borrow two elements through `immutable(T)*` pointers and return a negative,
zero, or positive `int`. They must provide a consistent ordering and must not
mutate the elements or collection through other aliases.

`MinIndex` and `MaxIndex` scan once and use exactly `Length - 1` comparisons for
nonempty input, keeping the first element when comparisons are equal. Empty
spans return zero without calling the comparator. Their indices borrow no value
ownership: the caller can inspect or explicitly copy the selected source element.
`SequenceCompare` stops at the first unequal pair, or after the shorter span's
length when one is an equal prefix. Its length-only results are -1, 0, or 1.
These three algorithms allocate nothing, copy no elements, and support immutable
views of noncopyable owning types. Comparator failures leave their sources
unchanged, provided the callback does not modify them through another alias.

For types implementing `IComparable<T>`, `Comparer<T>::Compare` supplies the
comparison callback, for example `Sort<int>(span, Comparer<int>::Compare)`.
`EqualityComparer<T>::Equals` similarly adapts `IEquatable<T>` for
`SequenceEqual`. These are static bridges and allocate no comparer objects.
Explicit custom callbacks remain available for other orderings or equality rules.

Equality searches borrow the key through `immutable(T)*`, so noncopyable owning
elements are supported without constructing or copying a search value. Mutable
spans can be searched through `AsImmutable()`. For example:

```csharp
int key = 5;
nuint first = IndexOf<int>(values.AsImmutable(), &key, EqualityComparer<int>::Equals);
nuint last = LastIndexOf<int>(values.AsImmutable(), &key, EqualityComparer<int>::Equals);
bool found = Contains<int>(values.AsImmutable(), &key, EqualityComparer<int>::Equals);
```

`IndexOf` and `Contains` compare from the beginning; `LastIndexOf` compares from
the end. They stop at the first match in that direction. Empty spans never call
the callback, and both index searches return zero for an empty span. All three
take O(n) comparisons and constant extra storage without allocating or modifying
elements. Callbacks may throw; the exception propagates without changing the
source. Callbacks must keep the span and key alive and must not mutate them
through another alias. These searches do not require sorted input.

`Rotate` takes a split index in `[0, Length]`: rotating `[1, 2, 3, 4, 5]` at
2 produces `[3, 4, 5, 1, 2]`. Zero and `Length` leave the span unchanged.
An out-of-range split throws before any mutation. Rotation takes linear time
and constant extra storage, using three reversals; it does not allocate, copy
elements, or run their destructors. Existing pointers still address the same
slots, whose values may change.

`Sort`, `Reverse`, and `Rotate` relocate values rather than copying them. Owning values
without copy constructors are supported; every value remains responsible for
its resources in its final slot. These operations do not allocate or free
storage and do not run element destructors. A comparator may allocate or throw.
If it throws during sorting, ordering may be partial but every element remains
alive and owned by the source collection.

`Partition` has the same ownership guarantee if its predicate throws. The
predicate borrows an element through `immutable(T)*` and runs once per element
unless an earlier invocation throws. `FindIndex` stops at the first match;
`IsSorted` stops at the first out-of-order adjacent pair. Empty spans are sorted
and compare equal to other empty spans without invoking callbacks.

## Reverse predicate search

`FindLastIndex` visits elements from the end toward the beginning, stopping at
the first true predicate result. An empty span returns zero without reading
element storage or invoking the predicate. The callback receives a borrowed
`immutable(T)*`; elements are never copied or destroyed by the search, including
noncopyable owners. Keep the span's backing owner alive and do not structurally
mutate it or change elements through another alias during the search. Predicate
exceptions propagate unchanged. The search itself does not allocate or alter the
input; callback side effects are not rolled back. The method is not `nothrow`,
because an arbitrary predicate can throw.

```csharp
using std; using std::Collections;

struct Record
{
    public int Code;
    public bool Failed;
    public Record(int code, bool failed) { Code = code; Failed = failed; }
}

bool IsFailure(immutable(Record)* value) => value.Failed;

int main()
{
    Record[4] data = [new Record(10, false), new Record(20, true),
        new Record(30, false), new Record(40, true)];
    Span<Record> records = new(data, 4);
    nuint index = FindLastIndex<Record>(records.AsImmutable(), IsFailure);
    if (index == records.Length) return 1;
    if (records[index].Code != 40) return 2;
    return 0;
}
```

## Shuffling

`Collections/span_shuffle.gf` supplies `Shuffle` separately from the comparison
algorithms. It uses a Fisher–Yates shuffle with unbiased bounded draws from
`PseudoRandom`. It takes linear time and constant extra storage, performs no
allocations, and relocates elements without copying them or running destructors.
Noncopyable owning values are supported.

```csharp
PseudoRandom random = new(42ul);
Shuffle<int>(values.AsSpan(), ref random);
```

The generator belongs to the caller and advances as the shuffle runs. Empty and
single-element spans do not advance it. The same starting elements and generator
state produce the same permutation. This is suitable for reproducible simulations
and games, not security-sensitive randomization. The borrowed span must remain
valid for the entire call; storage outside it is untouched.

Binary search requires input sorted by the same comparator. It returns the first
match among duplicates, uses O(log n) comparisons, and returns Length on a miss.
Use LowerBound instead when you need the insertion position for a missing key.
Empty spans do not call the comparator. Elements and the key are borrowed; a
throwing comparator propagates without modifying the span. The callback must
keep both alive and must not mutate either through another alias.

## Rank selection

Optional `Collections/nth_element.gf` adds the explicit-comparer `NthElement`
algorithm, reusing `Swap` and `Sort` from `Collections/span_algorithms.gf`. It relocates
values in place without copying owners or allocating heap scratch storage. On
success, the selected value has the specified sorted rank, with all earlier
values no greater and all later values no less. The partitions are not sorted
or stable. `nth == Length` is a no-op; larger indices throw before storage
access or comparison. Comparator failures can leave a rearranged initialized
permutation. Ordinary comparator and collector-hook effects still apply.
See [rank selection](../../../docs/NTH_ELEMENT.md) for the complete example,
boundaries, borrowing rules, and bounded heapsort-fallback complexity.

## Lexicographic arrangements

Optional `Collections/span_permutations.gf` provides `NextPermutation` and
`PreviousPermutation` for bounded arrangement searches. Both relocate owners
with public `Swap`/`Reverse`, use no heap scratch, and return false when wrapping.
All comparisons precede mutation, so comparator errors leave the arrangement
unchanged. See [lexicographic arrangements](../../../docs/PERMUTATIONS.md) for
ordering, duplicate-key and borrowing contracts and the complete example.

## Default ordering

The optional `Collections/default_ordering.gf` source adds `Sort<T>(Span<T>)` and
`StableSort<T>(Span<T>)` overloads for `T : IComparable<T>`. They forward to the
existing algorithms with `Comparer<T>::Compare`. They are ordinary namespace
functions, with the same ownership and failure rules as their explicit-comparator
counterparts.

```csharp
using std; using std::Collections;

int main()
{
    int[4] data = [3, 1, 2, 1];
    Span<int> values = new(data, 4);
    Sort(values);
    if (data[0] != 1 || data[1] != 1 || data[2] != 2 || data[3] != 3) return 1;
    StableSort(values);
    return 0;
}
```

Primitive ordering is the existing `IComparable<T>` policy: floating-point NaNs
compare before non-NaNs, all NaNs compare equal, and signed zeros compare equal.
StableSort preserves encounter order among such ties; Sort need not. Custom
comparers remain available through the two-argument overloads, without requiring
the interface.

For ordinary nonallocating comparisons, Sort allocates no storage and relocates
owners without copies. StableSort keeps its existing two-index-vector allocation
and comparison-before-relocation behavior. Comparison exceptions propagate;
Sort may leave a permutation partially ordered, whereas StableSort leaves the
input unchanged when allocation or comparison fails. User comparison methods
must obey the existing ordering, immutability and borrowed-pointer contracts.

Explicit source selection needs `Collections/default_ordering.gf`, `Collections/Comparer.gf`,
`IComparable.gf` and the normal Span dependencies. Also include the underlying
algorithm sources being used: `Collections/span_algorithms.gf` for Sort, and
`Collections/stable_sort.gf` with Vector dependencies for StableSort. Selecting the
original explicit-comparator sources alone does not gain a dependency on these
default-ordering contracts.

`default_ordering.gf` also provides `NthElement(values, nth)`,
`NextPermutation(values)`, and `PreviousPermutation(values)` for
`T : IComparable<T>`. These ordinary forwarders use `Comparer<T>::Compare` and
retain the respective explicit-comparer algorithms' ownership, comparison,
wrapping and failure rules. Include `Collections/nth_element.gf` for selection
or `Collections/span_permutations.gf` for the permutation operations, together with
`Collections/span_algorithms.gf` and the normal Span/Comparer/interface dependencies.
Unused generic wrappers do not require their underlying optional algorithm
modules. The explicit callbacks remain available without `default_ordering.gf`
or an `IComparable<T>` implementation.

### Default searches

The optional `default_ordering.gf` comparison-contract helpers also include:

| API | Default-order result |
| --- | --- |
| `LowerBound<T>(Span<immutable(T)>, key)` | First index not less than the borrowed key. |
| `UpperBound<T>(Span<immutable(T)>, key)` | First index greater than the borrowed key. |
| `BinarySearch<T>(Span<immutable(T)>, key)` | First equal index, or Length when absent. |
| `IsSorted<T>(Span<immutable(T)>)` | Whether every adjacent pair is nondecreasing. |

These ordinary overloads require `T : IComparable<T>` and forward through
`Comparer<T>::Compare` to the existing algorithms. Bounds return Length if their
insertion position is after the input. Empty searches return zero without reading
elements or comparing the key; IsSorted is true for empty and singleton spans.
Search input must already be sorted using this same default ordering. Equal
floating-point NaNs and signed zeros follow the existing comparison contract.

Elements and the key are borrowed, never copied, so noncopyable comparable owners
are supported. The algorithms allocate nothing internally; comparison methods can
allocate or throw and must obey the existing immutability/lifetime rules. Original
comparison exceptions propagate without changing input; their external side
effects are not rolled back. These helpers are ordinary throwing methods.

```csharp
using std; using std::Collections;

int main()
{
    int[5] data = [1, 2, 2, 4, 9];
    Span<int> values = new(data, 5);
    int key = 2;
    if (!IsSorted(values.AsImmutable())) return 1;
    nuint first = LowerBound(values.AsImmutable(), &key);
    nuint after = UpperBound(values.AsImmutable(), &key);
    nuint match = BinarySearch(values.AsImmutable(), &key);
    if (first != 1 || after != 3 || match != first) return 2;
    return 0;
}
```

For selective sources these wrappers need `Collections/default_ordering.gf`,
`Collections/span_algorithms.gf`, `Collections/Comparer.gf`, `IComparable.gf` and ordinary Span
dependencies. The explicit-comparator algorithm source remains independent of
DefaultOrdering/Comparer and still supports custom or descending comparisons.

### Default ordering and equality contracts

The optional `default_ordering.gf` conveniences also provide `MinIndex(values)`,
`MaxIndex(values)`, and `SequenceCompare(first, second)` for `IComparable<T>`.
They forward to the existing callback algorithms through `Comparer<T>::Compare`.
Min/max keep the first tied element and return `Length` for an empty span.
Sequence comparison returns the first nonzero comparison **without changing its
magnitude**; equal prefixes are ordered by length, returning -1, 0, or 1.

```cpp
using std; using std::Collections;
int main()
{
    int[4] data = [2, 5, 5, 1];
    Span<int> values = new(data, 4);
    nuint minimum = MinIndex(values.AsImmutable()); // 3
    nuint maximum = MaxIndex(values.AsImmutable()); // 1: first equal maximum
    int order = SequenceCompare(values.Slice(0, 1).AsImmutable(), values.AsImmutable()); // -1
    if (minimum != 3 || maximum != 1 || order != -1) return 1;
    return 0;
}
```

The optional `default_equality.gf` provides `SequenceEqual(first, second)`,
`IndexOf(values, key)`, `LastIndexOf(values, key)`, and `Contains(values, key)`
for `IEquatable<T>`, forwarding through `EqualityComparer<T>::Equals`.
Searches borrow the key and elements. `IndexOf` and `LastIndexOf` return
`Length` if no match exists; `Contains` returns false.
Sequence equality rejects unequal lengths before comparing any elements.
Default floating equality treats NaNs as equal and signed zeros as equal.

```cpp
using std; using std::Collections;
int main()
{
    int[4] data = [1, 2, 2, 4];
    Span<int> values = new(data, 4);
    int key = 2;
    nuint first = IndexOf(values.AsImmutable(), &key); // 1
    nuint last = LastIndexOf(values.AsImmutable(), &key); // 2
    if (!Contains(values.AsImmutable(), &key)) return 1;
    if (!SequenceEqual(values.AsImmutable(), values.AsImmutable())) return 2;
    if (first != 1 || last != 2) return 3;
    return 0;
}
```

These helpers do not allocate or copy elements themselves. User comparisons
and equality methods can allocate or throw; their original exceptions propagate,
and these read-only algorithms do not modify either input. Existing overloads
with explicit callbacks remain available without either optional source file.
Selective source users need `span_algorithms.gf`, `Span` and its dependencies, the
appropriate `IComparable`/`Comparer` or `IEquatable`/`EqualityComparer` pair, and
the chosen default convenience file. Ordinary std projects include them already.
