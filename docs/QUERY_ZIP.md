# Pairwise lazy queries

`left.Zip(right, selector)` combines corresponding elements from two concrete
`Enumerable` values and stops at the shorter input. The selector supplies the
result type, so no pair or tuple allocation is needed.

```csharp
using std; using std::Queries;
int[3] numbers = [1, 2, 3];
int[2] offsets = [10, 20];
Span<int> first = new(numbers, 3);
Span<int> second = new(offsets, 2);
var sums = first.AsEnumerable().Zip(second.AsEnumerable(),
    (int value, int offset) => value + offset);
// Enumerating sums produces 11, 22.
```

The inputs can have different element, source, and iterator types. The selector
uses the existing noncapturing function-pointer callback rules. Named functions
can return owning values such as String. Include `Queries/EnumerableZip.gf`
and `Queries/ZipSource.gf` with the ordinary enumerable interfaces and sources
when selecting library files individually.

## Evaluation

Constructing the query copies its source views without enumerating either input.
Each `GetEnumerator()` constructs the left iterator, then the right iterator.
Both exist even when one input is empty or a later `Take(0)` avoids advancing.
Every `MoveNext()` advances the left input first. If it ends, the right input is
not advanced; otherwise the right input is advanced. Consequently, when the
right input ends first, the left iterator may advance once without a matching
right element. This can run input callbacks or expose an input error. After
ordinary exhaustion, further calls advance neither input.

`Current` invokes the selector for the current pair, following the library's
existing `Select` behavior. Reading `Current` twice invokes it twice and creates
two ordinary results. `Count()` and `Any()` can advance without invoking the
selector. `Current` does not cache projected results and throws before the first
successful move and after exhaustion.
Each enumeration starts fresh and has independent iterator positions.

## Ownership and failures

Built-in borrowed source views and Zip's state allocate nothing. Callbacks,
element/source copies, custom iterators, or materialized results may allocate.
Sources follow the existing copyable-source contract; iterators may own resources
and be noncopyable. Zip moves each freshly constructed iterator into its state.
If constructing the right iterator throws, the left iterator is cleaned up.
The nested iterator owns both input iterators. It releases them when it leaves
scope, including cleanup after terminal operations, early return, `break`, and
exceptions. An owning selector result follows normal value semantics
and is cleaned up or transferred by its caller.

Selector parameters also follow ordinary value semantics. Reading String
elements from either input copies them before passing them to a by-value String
parameter; editing those copies leaves the source strings unchanged. `Count()`
does not read the paired elements and therefore does not perform those copies.

Input and callback exceptions propagate unchanged. There is no rollback of
completed advances or callback effects, and resuming after a failed `MoveNext`
has no recovery guarantee. Discard that iterator after such an error.

Zip retains the source views' borrowing rules. Keep backing storage alive and
stable for the query and all iterators; pointer results do not extend an owner's
lifetime. Writes through other valid aliases remain visible when their elements
are read. See [query ownership](../std/core/Queries/README.md#ownership).
