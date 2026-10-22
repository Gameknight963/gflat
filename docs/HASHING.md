# Equality, hashing, and ordering

Core provides three independent contracts in `std`:

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
Signed 128-bit `extralong` participates as well; its hash folds the high and low
64-bit halves before applying the ordinary integer mixer.
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
`Hashing::Integer(ulong)` mixes all 64 bits into a 32-bit result, also returned
as `nuint`. Neither operation allocates. Hash values are implementation details,
not persistent identifiers, cryptographic digests, or protection against
adversarial collision attacks.
