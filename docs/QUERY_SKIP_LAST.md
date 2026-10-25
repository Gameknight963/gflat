# Streaming without a trailing suffix

`SkipLast(count)` yields input values in order except for its final at most
`count` values. It is useful for forwarding a record stream while omitting a
fixed footer. Unlike reversing or materializing a whole input, it keeps only a
bounded lookahead and can yield from an infinite source after that initial lag.

```csharp
using std;
int main()
{
    int[5] data = [1, 2, 3, 4, 5];
    Span<int> values = new(data, 5);
    int expected = 1;
    foreach (int value in values.AsEnumerable().SkipLast(2))
    {
        if (value != expected) return 1;
        expected++;
    }
    return expected - 4;
}
```

Creating the query does not enumerate input. Each enumerator owns an upstream
iterator, constructed by `GetEnumerator`, and an initially empty `Deque<T>`.
Factory and ordinary source-copy effects can therefore occur even for
`SkipLast(count).Take(0)`. That combination does not call upstream `MoveNext` or
`Current`, or allocate lookahead storage.

For positive counts, the first successful move needs `count` withheld values
plus the current output. Each later successful move reads one more input value.
Every buffered upstream `Current` is read once. `SkipLast(n).Take(k)` with
positive `k` consumes at most `k + n` input values, or all input if it ends first;
this is a mathematical count, not unchecked pointer-sized addition. If the input
has at most `count` values, it is consumed fully and produces no output.
A huge count does not reserve that amount upfront: Deque grows only as observed
input arrives, subject to its checked capacity limits and geometric growth.
Storage is O(min(input length, count + 1)), including the current buffered output.

`Current` copies the buffered front by ordinary value rules. Repeated reads can
copy owning values again. The next move destroys that previous buffered front.
At EOF, the withheld tail is destroyed immediately. Early iterator destruction
releases every pending value and the upstream iterator. `SkipLast(0)` instead
forwards moves and `Current` without allocating a Deque buffer; repeated Current
reads then have the upstream getter's ordinary effects.

Retained values must support ordinary copying from the buffer, including when
the runtime count is zero. This does not consume borrowed owner headers, support
retained noncopyable values, or strip outer readonly/immutable qualification.
Pointer and managed-reference elements retain their ordinary target sharing;
owning Strings are copied independently. Custom copy/destructor effects remain
ordinary element effects. The wrapper does not structurally mutate source values.

Keep borrowed source backing storage alive. Source values and their captured
views follow normal copy rules. Consumable file sources advance their shared
file position by the lookahead and are not rewound. Stopping early can leave
unread input; a new enumeration starts wherever that source now starts, without
replaying discarded buffered values. Reiterable sources get independent cursors.

Move, buffering-copy and allocation errors propagate and make this iterator
finished: later moves return false without retrying, and Current is invalid.
Pending owners and the upstream cursor are released when the iterator is
destroyed. A buffered output-copy failure in `Current` leaves position unchanged
and can be retried; zero-count getter failures have the upstream getter's policy.
No new runtime hooks or GC objects are introduced. Selective builds include
`Collections/EnumerableSkipLast.gf`, `Collections/SkipLastSource.gf`, the ordinary
Enumerable contracts, and Deque with its normal dependencies.
