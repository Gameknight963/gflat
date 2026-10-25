# Taking a finite sequence's tail

`TakeLast(count)` yields the final at most `count` values of a finite input, in
original order. It does not reorder or write through the input values. Each
enumerator owns
an independent `Deque<T>` tail buffer.

```csharp
using std; using std::Queries;
int main()
{
    int[5] input = [1, 2, 3, 4, 5];
    Span<int> values = new(input, 5);
    int expected = 4;
    foreach (int value in values.AsEnumerable().TakeLast(2))
    {
        if (value != expected) return 1;
        expected++;
    }
    return expected - 6;
}
```

Creating the query or its enumerator does not enumerate upstream or allocate
the element buffer. Custom source-value copies retain their ordinary effects.
`TakeLast(0)` never constructs or advances the upstream iterator, reads its
`Current`, or allocates tail storage. Ordinary source copies may still occur.
For positive counts, the first `MoveNext` consumes the whole input and disposes
the upstream iterator before yielding. An infinite source cannot yield a tail.
`TakeLast(2).Take(1)` still consumes the complete source;
`TakeLast(2).Take(0)` does not start buffering.

Once the tail fills, each next successful upstream move discards the oldest
buffered value before reading and retaining the new `Current`. Each candidate's
`Current` is read once during buffering. Deque eviction transfers and destroys
the discarded value without copying it. Buffer growth uses existing checked
Deque allocation, never an upfront reservation of the requested count. A huge
count on a short input therefore stores only that input, rather than requesting
storage proportional to the count.

`Current` returns an ordinary `T` value copied from the buffered position, like
`Reverse()`. Repeated reads can copy an owning value again. Elements must permit
ordinary copying from retained storage; this is not a way to consume a buffered
noncopyable owner or to strip outer readonly/immutable element qualification.
Pointer and managed-reference values keep their ordinary shared targets, while
owning Strings retain independent copies. Project qualified scalar values to an
explicit mutable stored type when necessary, as with `Reverse`/`ToVector`.
Keep borrowed source storage alive and obey its borrowing rules. Consumable
sources such as `FileLineSequence` still consume their shared file position; this
wrapper does not rewind a file or make a one-shot source repeatable. A new enumeration
reads the source again and owns a separate tail.

Input enumeration, copying, or allocation errors propagate. Failed initialization
cleans the staged tail and upstream iterator and leaves this iterator finished:
later moves return false without retrying, and `Current` is invalid. An output
`Current` copy failure leaves the successful buffered position unchanged and can
be retried. A successful tail remains owned until enumerator destruction,
including after exhaustion, and normal loop/exception cleanup releases it.

Buffer storage is O(min(count, input length)), subject to Deque's normal geometric
capacity growth; buffering performs linear work and later moves are constant-time.
Element copy/allocation/destruction costs remain their normal costs. Unlike
`Reverse().Take(count).Reverse()`, this retains only a bounded tail instead of
materializing every input element. It does not change existing tail tools.
Selective source users include `Queries/enumerable_take_last.gf`,
`Queries/TakeLastSource.gf`, the ordinary Enumerable/enumeration contracts,
and `Deque<T>` plus its normal Span dependencies. No new runtime hooks or GC
allocation are introduced by the wrapper.
