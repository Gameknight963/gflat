# Equality, hashing, and ordering

Core provides three independent contracts in `Standard`:

- `IHashable.GetHashCode()` returns `nuint`.
- `IEquatable<T>.Equals(immutable(T)* other)` compares borrowed values.
- `IComparable<T>.CompareTo(immutable(T)* other)` returns a negative value, zero,
  or a positive value for less than, equal to, or greater than its argument.

All three methods have immutable receivers. Equality and comparison borrow their
argument, so comparing owning values does not copy their resources.

Each primitive scalar participates through an implementation declared inside
the corresponding interface. This includes booleans, characters, signed and
unsigned integers, pointer-sized integers, and floating-point values. The
primitive type remains built in and does not depend on these interfaces existing.
Signed 128-bit `extralong` participates as well; its hash processes all 16 bytes
with FNV-1a, then applies the integer mixer. The bytes use native byte order;
this is not a portable serialized hash format.
Character ordering uses unsigned byte values (0 through 255); boolean ordering
puts false before true.

Equal keys must produce equal hashes. Hash collisions are permitted. A key's
equality and hash must remain stable while it belongs to a hash collection.

## Floating-point values

Interface equality treats every NaN as equal to every other NaN, and treats
positive and negative zero as equal. This differs from the built-in floating
`==` operator for NaN. Hashes canonicalize both cases to preserve the equality
contract. Ordering puts NaN before every non-NaN; NaNs compare equal to each
other. Finite values and infinities use their ordinary numerical order.

## Strings

`String` compares its counted UTF-8 bytes, including embedded null bytes.
Comparison is ordinal and locale-independent, with unsigned byte ordering.
It does not normalize Unicode or ignore letter case. Equal contents compare
equal even when their buffers and capacities differ.

`Hashing::Bytes(data, count)` computes deterministic 32-bit FNV-1a and returns
it as `nuint`. `String.GetHashCode()` uses this function over its full length.
`Hashing::Integer(ulong)` applies a 64-bit avalanche before folding into a
32-bit result, also returned
as `nuint`. Neither operation allocates. Hash values are implementation details,
not persistent identifiers, cryptographic digests, or protection against
adversarial collision attacks.

## Combining component hashes

`Hashing::Combine(nuint first, nuint second) nothrow` combines two component
hashes in order, without allocation. Both arguments retain their full
pointer-sized width during mixing; the result is a 32-bit hash returned as
`nuint`, like `Integer`. For example, a coordinate key can hash each field
before combining:

```csharp
using Standard;
struct Point : IHashable, IEquatable<Point> {
    public int X;
    public int Y;
    public Point(int x, int y) { X = x; Y = y; }
    public immutable bool Equals(immutable(Point)* other)
        => X == other.X && Y == other.Y;
    public immutable nuint GetHashCode() nothrow
        => Standard::Hashing::Combine(Standard::Hashing::Integer((ulong)X),
                                Standard::Hashing::Integer((ulong)Y));
}
```

Equal component hashes produce equal combined hashes. Order participates in
mixing, so `(x, y)` is not deliberately treated as `(y, x)`. Collisions are still
possible, including between swapped pairs or different components. Combining
cannot recover information already lost by a component's own hash.
For more fields, nest `Combine` in a fixed order; it is not an associative
operation or a serialized representation. As with the other helpers, results
are implementation details, not persistent identifiers, cryptographic digests,
or protection against adversarial keys. No pointers or objects are retained.
