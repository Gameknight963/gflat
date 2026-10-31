# Equality, hashing, and ordering

`Standard` defines collection contracts with immutable receivers:

| Interface method | Result |
| --- | --- |
| `IHashable.GetHashCode()` | `nuint` hash |
| `IEquatable<T>.Equals(immutable(T)* other)` | Value equality |
| `IComparable<T>.CompareTo(immutable(T)* other)` | Negative, zero, or positive for less, equal, or greater |

Primitive scalars and String provide implementations. Equal values must have
equal hashes. A key's equality and hash must remain stable while stored in a
hash collection. Character ordering uses unsigned bytes, and Boolean ordering
puts false before true.

## Floating-point values

Interface equality equates all NaNs and both signed zeros. This differs from
floating `==`, where a NaN is unequal to itself. Hashes canonicalize those cases.
Ordering puts NaNs before all other values, then uses numerical order.

## Strings

String equality and ordering use complete counted bytes, including NULs.
Ordering is unsigned lexicographic order. `Hashing::Bytes(data, count)` hashes
those bytes with 32-bit FNV-1a and returns `nuint`. `Hashing::Integer(ulong)`
mixes integer bits into a 32-bit hash returned as `nuint`.

Hash values serve collection lookup and can change between library versions.
Use a persistent representation of the value itself when saving keys.
Ordinary collection hashes are vulnerable to deliberately chosen collisions.

## Combining component hashes

`Hashing::Combine(first, second)` combines two hashes in order:

```csharp
using Standard;

struct Point : IHashable, IEquatable<Point> {
    public int X;
    public int Y;
    public Point(int x, int y) { X = x; Y = y; }
    public immutable bool Equals(immutable(Point)* other)
        => X == other.X && Y == other.Y;
    public immutable nuint GetHashCode() nothrow
        => Hashing::Combine(Hashing::Integer((ulong)X),
                                Hashing::Integer((ulong)Y));
}
```

For more fields, nest `Combine` in a fixed order. Collisions remain possible,
and the operation is not associative.
