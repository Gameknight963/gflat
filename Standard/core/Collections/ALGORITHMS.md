# Value and span algorithms

Import `Standard::Collections` for algorithms over initialized values and spans:

```csharp
using Standard;
using Standard::Collections;

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

## Comparisons

Comparators have type `int(immutable(T)*, immutable(T)*)*`. Return a negative
value for less, zero for equivalent, and a positive value for greater. Equality
callbacks return bool. Predicates receive one `immutable(T)*`.

Keep ordering consistent throughout an operation. Callbacks must leave input
storage and keys alive and structurally unchanged. Callback exceptions propagate.
Effects performed by the callback itself remain visible after failure.

Read-only searches inspect elements through borrowed addresses. Mutating
algorithms relocate initialized values, including noncopyable owners. Relocation
changes which value occupies a slot, so pointers to a slot can observe a different
value afterward. Indirect pointers stored inside an element retain their addresses.

Allocation and comparison counts are listed where they affect algorithm choice.
Callbacks and managed-reference bookkeeping retain their own costs.

## Swapping values

`Swap(ref left, ref right)` exchanges two initialized mutable values by relocation.
Passing the same storage is a no-op. Separate fields or elements can be swapped,
but partially overlapping unsafe aliases are invalid. Both argument expressions
are evaluated before the operation begins.

Pointers to the two slots stay at those addresses. Ownership of separately
allocated buffers follows the exchanged values.

## Bounds and equal ranges

All three methods require input sorted under the supplied comparator:

| Method | Result |
| --- | --- |
| `LowerBound(values, key, compare)` | First index whose element is at least the key |
| `UpperBound(values, key, compare)` | First index whose element is greater than the key |
| `EqualRange(values, key, compare)` | Borrowed span between those two bounds |

A missing bound returns `Length`, including zero for an empty span. Each bound
uses logarithmic comparisons. EqualRange uses both searches and returns a null
Data pointer with zero length when empty. Its key can alias an input element.
Use the bounds directly when an insertion index is needed.

### Minimum, maximum, and lexicographic order

`MinIndex` and `MaxIndex` return the first least or greatest element's index,
or Length for an empty span. Nonempty input uses `Length - 1` comparisons.

`SequenceCompare(first, second, compare)` compares corresponding elements until a
nonzero result, preserving that result's magnitude. An equal prefix is ordered
by length, returning -1, 0, or 1.

### Common prefixes

`CommonPrefixLength` returns the number of matching elements from the beginning.
It stops at the first unequal pair or the shorter length. Compare the lengths
as well when checking complete sequence equality.

### Equality searches

```csharp
int key = 5;
nuint first = IndexOf<int>(values.AsImmutable(), &key, EqualityComparer<int>::Equals);
nuint last = LastIndexOf<int>(values.AsImmutable(), &key, EqualityComparer<int>::Equals);
bool found = Contains<int>(values.AsImmutable(), &key, EqualityComparer<int>::Equals);
```

| Method | Search order and result |
| --- | --- |
| `IndexOf(values, key, equal)` | First matching index, searched from the beginning |
| `LastIndexOf(values, key, equal)` | Last matching index, searched from the end |
| `Contains(values, key, equal)` | True at the first match |
| `SequenceEqual(first, second, equal)` | Equal lengths and all corresponding elements equal |

Missing index searches return Length. SequenceEqual rejects different lengths
before comparing elements. Searches take a borrowed key and accept unsorted input.

### Predicates

| Method | Result |
| --- | --- |
| `AllOf(values, predicate)` | False at the first rejected element, true for empty input |
| `AnyOf(values, predicate)` | True at the first accepted element, false for empty input |
| `NoneOf(values, predicate)` | False at the first accepted element, true for empty input |
| `CountIf(values, predicate)` | Count of accepted elements in a complete left-to-right scan |
| `FindIndex(values, predicate)` | First accepted index, or Length |

These scans use at most one call per element in encounter order. Their callbacks
borrow elements, allowing inspection of noncopyable owners.

## Rotation and relocation

`Rotate(values, middle)` places the prefix after the suffix, preserving each
part's order. Rotating `[1,2,3,4,5]` at 2 produces `[3,4,5,1,2]`. Zero and Length
leave it unchanged. A larger split throws before mutation.

`Reverse(values)` reverses in place. `Sort(values, compare)` uses ascending
heapsort with O(n log n) comparisons and constant extra storage. Sort is unstable.
Comparator failure can leave a partially ordered permutation with all owners
still initialized.

`Partition(values, predicate)` moves matching values into a prefix and returns
its length. Relative order is unstable. Predicate failure retains an initialized
permutation of the original owners.

## Partition validation

`IsPartitioned` checks that every true predicate result precedes every false
result. It scans left to right, returning false at the first true-after-false
transition. Empty, all-true, and all-false input returns true.

```csharp
using Standard;
using Standard::Memory;
using Standard::Collections;

bool Positive(immutable(int)* value) => *value > 0;

int main()
{
    int[5] data = [4, 2, 0, -1, -3];
    Span<int> values = new(data, 5);
    if (!IsPartitioned<int>(values.AsImmutable(), Positive)) return 1;
    if (PartitionPoint<int>(values.AsImmutable(), Positive) != 2) return 2;
    int[3] mixed = [1, 0, 2];
    Span<int> invalid = new(mixed, 3);
    if (IsPartitioned<int>(invalid.AsImmutable(), Positive)) return 3;
    return 0;
}
```

## Partition boundary search

`PartitionPoint` returns the first false index in an already partitioned span.
All-true input returns Length. All-false and empty input returns zero. It makes
at most `floor(log2(Length)) + 1` predicate calls for nonempty input.

```csharp
using Standard;
using Standard::Memory;
using Standard::Collections;

struct Record
{
    public int Code;
    public bool Complete;
    public Record(int code, bool complete) { Code = code; Complete = complete; }
}

bool IsComplete(immutable(Record)* value) => value.Complete;

int main()
{
    Record[5] data = [new Record(8, true), new Record(3, true),
        new Record(5, true), new Record(9, false), new Record(1, false)];
    Span<Record> records = new(data, 5);
    nuint boundary = PartitionPoint<Record>(records.AsImmutable(), IsComplete);
    if (boundary != 3 || records[boundary].Code != 9) return 1;
    return 0;
}
```

Supply a consistent predicate and a true-prefix/false-suffix partition. The search
assumes that precondition. Use IsPartitioned for an explicit validation pass.

## Adjacent pair search

`AdjacentFind` examines pairs from left to right and returns the first matching
pair's left index, or Length. Empty and singleton spans have no pairs.
The callback receives the previous element first.

```csharp
using Standard;
using Standard::Memory;
using Standard::Collections;

bool Drops(immutable(int)* left, immutable(int)* right) => *left > *right;

int main()
{
    int[5] data = [1, 2, 2, 4, 3];
    Span<int> values = new(data, 5);
    nuint duplicate = AdjacentFind<int>(values.AsImmutable());
    nuint drop = AdjacentFind<int>(values.AsImmutable(), Drops);
    if (duplicate != 1 || drop != 3) return 1;
    return 0;
}
```

The default equality overload finds equal neighbors. A custom predicate can
identify another transition, such as a descending pair.

## Reverse predicate search

`FindLastIndex` scans from the end toward the beginning and returns the first
accepted index in that direction, or Length.

```csharp
using Standard;
using Standard::Memory;
using Standard::Collections;

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

`Shuffle(values, ref random)` uses Fisher-Yates with the supplied PseudoRandom.
It takes linear time and constant extra storage.

```csharp
using Standard::Numerics;

PseudoRandom random = new(42ul);
Shuffle<int>(values.AsSpan(), ref random);
```

Empty and singleton spans leave the generator unchanged. The same input and
initial generator state produce the same permutation. Use it for reproducible
simulation and games. Security-sensitive sampling needs a suitable random source.

## Binary search

`BinarySearch(values, key, compare)` returns the first matching index among sorted
elements, or Length when absent. It uses O(log n) comparisons. Input must use the
same ordering as the search. LowerBound supplies an insertion position for a
missing key.

## Rank selection

`NthElement(values, nth, compare)` places a value at its sorted rank, with earlier
values no greater and later values no less. The two partitions remain otherwise
unordered. `nth == Length` is a no-op. Larger indices throw before comparison.
Comparator failure can leave a rearranged initialized permutation.

See [rank selection](../../../docs/NTH_ELEMENT.md) for complexity and examples.

## Lexicographic arrangements

`NextPermutation` and `PreviousPermutation` advance lexicographic order. At an end
they wrap to the opposite ordering and return false. Comparisons finish before
relocation, so comparison failure preserves the arrangement.

See [permutations](../../../docs/PERMUTATIONS.md).

## Default ordering

Overloads without an explicit comparator use `IComparable<T>` through
`Comparer<T>::Compare`. For example:

```csharp
using Standard;
using Standard::Memory;
using Standard::Collections;

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

Default floating-point ordering puts NaNs before other values, treats all NaNs
as equivalent, and treats signed zeros as equivalent. StableSort preserves
encounter order among ties.

Sort uses constant extra storage. StableSort allocates two index buffers and
finishes comparisons before relocating elements. Allocation or comparison
failure therefore preserves StableSort's input, while Sort can leave a partial
ordering. See [stable sorting](OWNING_COLLECTIONS.md#stable-sorting).

### Default searches

LowerBound, UpperBound, BinarySearch, and IsSorted also accept the default
IComparable ordering:

```csharp
using Standard;
using Standard::Memory;
using Standard::Collections;

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

`IsSorted` accepts nondecreasing order, including equal neighbors. Empty and
singleton spans are sorted.

### Default ordering and equality contracts

The same comparison contract supplies MinIndex, MaxIndex, and SequenceCompare:

```cpp
using Standard;
using Standard::Memory;
using Standard::Collections;

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

Equality overloads use `IEquatable<T>` through `EqualityComparer<T>::Equals`:

```cpp
using Standard;
using Standard::Memory;
using Standard::Collections;

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

Default floating equality treats NaNs as equal and signed zeros as equal.
Explicit callbacks can supply other ordering and equality rules.

## Prefix and suffix matching

`StartsWith(values, pattern, equal)` and `EndsWith(values, pattern, equal)` compare
the leading or trailing region. Empty patterns match. A longer pattern returns
false before comparing. Comparison runs left to right within the selected region,
source address first and pattern address second.

```csharp
using Standard;
using Standard::Memory;
using Standard::Collections;
using Standard::Text;

bool EqualInt(immutable(int)* left, immutable(int)* right) => *left == *right;

int main()
{
    int[5] data = [10, 20, 30, 40, 50];
    int[2] first = [10, 20];
    int[2] last = [40, 50];
    Span<int> values = new(data, 5);
    Span<int> prefix = new(first, 2);
    Span<int> suffix = new(last, 2);
    if (!StartsWith<int>(values.AsImmutable(), prefix.AsImmutable())) return 1;
    if (!EndsWith<int>(values.AsImmutable(), suffix.AsImmutable(), EqualInt)) return 2;
    char[4] header = ['R', 'I', 'F', 'F'];
    Span<char> text = new(header, 4);
    if (!text.AsReadOnly().StartsWith("RI")) return 3;
    return 0;
}
```

These are Collections free functions. The Text span extensions also accept
C-string character patterns, as shown above.

## Consecutive run search

`FindRun(values, count, key, equal)` returns the first run of `count` matching
elements, or Length. Zero count returns zero. A count larger than Length returns
Length. It examines elements once from left to right, with the element address
first and key second. The callback can be asymmetric.

```csharp
using Standard;
using Standard::Memory;
using Standard::Collections;

bool EqualInt(immutable(int)* element, immutable(int)* key) => *element == *key;

int main()
{
    int[7] data = [4, 4, 1, 4, 4, 4, 2];
    Span<int> values = new(data, 7);
    int key = 4;
    if (FindRun<int>(values.AsImmutable(), 3, &key) != 3) return 1;
    if (FindRun<int>(values.AsImmutable(), 2, &key, EqualInt) != 0) return 2;
    if (FindRun<int>(values.AsImmutable(), 0, &key) != 0) return 3;
    if (FindRun<int>(values.AsImmutable(), 8, &key) != 7) return 4;
    return 0;
}
```

## Contiguous pattern search

`IndexOf(values, pattern, equal)` returns the first contiguous match, or Length.
An empty pattern returns zero, and an oversized pattern returns Length.
Candidates and their offsets are compared left to right, source address first.
Worst-case work is O(N*M), with constant extra storage.

```csharp
using Standard;
using Standard::Memory;
using Standard::Collections;

bool EqualInt(immutable(int)* element, immutable(int)* pattern) => *element == *pattern;

int main()
{
    int[7] data = [1, 2, 1, 2, 3, 2, 3];
    int[3] sought = [1, 2, 3];
    Span<int> values = new(data, 7);
    Span<int> pattern = new(sought, 3);
    if (IndexOf<int>(values.AsImmutable(), pattern.AsImmutable()) != 2) return 1;
    if (IndexOf<int>(values.AsImmutable(), values.Slice(3, 2).AsImmutable(), EqualInt) != 3) return 2;
    int key = 2;
    if (IndexOf<int>(values.AsImmutable(), &key) != 1) return 3;
    Span<immutable(int)> empty = new();
    if (IndexOf<int>(values.AsImmutable(), empty) != 0) return 4;
    return 0;
}
```

Input and pattern may overlap or be identical. Pass a span for a pattern and a
pointer for a single key.

## Last contiguous pattern match

`LastIndexOf(values, pattern, equal)` tries candidate starts from the final possible
start down to zero. Within each candidate it compares offsets left to right,
source address first. Missing or oversized patterns return Length. An empty
pattern also returns Length, its final insertion position.

```csharp
using Standard;
using Standard::Memory;
using Standard::Collections;

bool EqualInt(immutable(int)* element, immutable(int)* pattern) => *element == *pattern;

int main()
{
    int[7] data = [1, 2, 3, 1, 2, 0, 1];
    int[2] sought = [1, 2];
    Span<int> values = new(data, 7);
    Span<int> pattern = new(sought, 2);
    if (LastIndexOf<int>(values.AsImmutable(), pattern.AsImmutable()) != 3) return 1;
    if (LastIndexOf<int>(values.AsImmutable(), values.Slice(0, 3).AsImmutable(), EqualInt) != 0) return 2;
    int key = 1;
    if (LastIndexOf<int>(values.AsImmutable(), &key) != 6) return 3;
    Span<immutable(int)> empty = new();
    if (LastIndexOf<int>(values.AsImmutable(), empty) != 7) return 4;
    return 0;
}
```

Worst-case work is O(N*M), with constant extra storage. Overlapping views are valid.

## First ordering violation

`FindUnsortedIndex(values, compare)` returns the first `i` for which
`compare(&values[i-1], &values[i]) > 0`, or Length. Empty and singleton input
returns Length. For `[1,3,2,4]`, the result is 2 and the prefix `[0,2)` is sorted.

It checks adjacent pairs left to right, previous element first, until a violation.

## Sorted multiset inclusion

`Includes(values, required, compare)` checks comparison-equivalent multiplicities
in two sorted spans. `[17,17,42]` includes `[17,17]` and `[17,42]`. It fails for
`[17,17,17]`. Matching values may have gaps.

Both inputs must use the same sorted ordering. Empty required input returns true,
and oversized required input returns false. The scan compares source first,
required second. A negative result advances source, zero advances both, and a
positive result fails. Work is O(N+M).

## Stable partitioning

`StablePartition(values, predicate)` moves matches to a prefix while preserving
encounter order within both groups. It returns the matching count. Each original
element is tested once, left to right.

Balanced recursive partitions are joined with Rotate. This uses O(n log n)
relocations and O(log n) stack space. Predicate failure can leave completed
subranges rearranged, with every original value still initialized and owned.
Use Partition when preserving order is unnecessary and constant extra storage
is preferred.

## Borrowed max heaps

The greatest element under the comparator occupies the first slot:

| Method | Precondition and effect |
| --- | --- |
| `MakeMaxHeap` | Builds a heap from arbitrary elements in O(n) comparisons |
| `PushMaxHeap` | The prefix excluding the final element is already a heap. Incorporates that element in O(log n) |
| `PopMaxHeap` | Input is a heap. Moves its maximum to the final slot and heap-orders the prefix in O(log n) |
| `IsMaxHeap` | Checks parent/child ordering, stopping at the first violation |

```csharp
using Standard::Memory;
using Standard::Collections;

int CompareHeapValues(immutable(int)* left, immutable(int)* right)
{
    if (*left < *right) return -1;
    if (*left > *right) return 1;
    return 0;
}

int main()
{
    int[5] data = [1, 9, 3, 2, 5];
    Span<int> values = new(data, 5);
    MakeMaxHeap<int>(values, CompareHeapValues);
    if (data[0] != 9) return 1;
    PopMaxHeap<int>(values, CompareHeapValues);
    int maximum = data[4];
    if (!IsMaxHeap<int>(values.Slice(0, 4).AsImmutable(), CompareHeapValues)) return 2;
    data[4] = 7;
    PushMaxHeap<int>(values, CompareHeapValues);
    if (maximum != 9 || data[0] != 7 || values.Length != 5) return 3;
    return 0;
}
```

PopMaxHeap keeps the same initialized elements and length. Shorten the view or
remove the final vector element afterward. Empty and singleton input needs no
comparisons. A failing comparator can leave a partial ordering, with all values
still owned. PriorityQueue uses a min-heap for its owning API.

## Common suffixes

`CommonSuffixLength` compares trailing elements from right to left until the
first mismatch or shorter length. For a changed middle, find the common prefix,
then inspect suffixes of the remaining slices so the unchanged ends cannot overlap.

## Source selection

Ordinary Standard projects include these sources. Selective builds add Span and
its contracts, then the relevant algorithm files:

| Source | Operations |
| --- | --- |
| `Collections/span_algorithms.gf` | Swap, Sort, Reverse, Rotate, bounds, scalar-key searches, predicates, and heaps |
| `Collections/default_ordering.gf` | IComparable overloads, with Comparer and IComparable |
| `Collections/default_equality.gf` | IEquatable overloads, with EqualityComparer and IEquatable |
| `Collections/stable_sort.gf` | StableSort, with Vector dependencies |
| `Collections/nth_element.gf` | NthElement |
| `Collections/span_permutations.gf` | NextPermutation and PreviousPermutation |
| `Collections/span_shuffle.gf` | Shuffle, with PseudoRandom |
| `Collections/span_find_run.gf` | FindRun |
| `Collections/span_subsequence_search.gf` | Pattern IndexOf and LastIndexOf |
| `Collections/span_stable_partition.gf` | StablePartition |

Default overloads also need the underlying algorithm source when called.
