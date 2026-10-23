# BigInteger

`std::BigInteger` is an owning signed integer whose precision is limited by
available storage. It is an ordinary library value, using `Vector<uint>` decimal
limbs; it requires no GC or compiler-specific arithmetic support.

```csharp
using std;

int main()
{
    BigInteger factorial = new BigInteger(1);
    for (long factor = 2l; factor <= 100l; factor++)
    {
        BigInteger operand = new BigInteger(factor);
        factorial = factorial.Multiply(&operand);
    }
    factorial.Format(Console::Out);
    Console::WriteLine();
    Console::Flush();
    return 0;
}
```

This prints 100 factorial. `Multiply` borrows its operands and returns a fresh
owner, which the assignment transfers into `factorial`. The old value is destroyed
normally. Streaming `Format` does not allocate a temporary decimal string.

## Operations

| API | Behavior |
| --- | --- |
| `new BigInteger()` | Zero, without allocation. |
| `new BigInteger(long)` | Exact signed 64-bit value, including `long` minimum. |
| `BigInteger::Parse(text)` | Strict, null-terminated decimal input. |
| `BigInteger::Parse(data, count)` | Strict, counted decimal input; no terminator is required. |
| `BigInteger::TryParse(data, count, ref result)` | Parse counted decimal input into an initialized owning result; false resets it to zero. |
| `BigInteger::TryParse(text, ref result)` | The same syntax and result contract for null-terminated input. |
| `Sign` | -1, 0, or 1. |
| `Add(immutable(BigInteger)* other)` | Return the exact sum. |
| `Subtract(immutable(BigInteger)* other)` | Return this value minus `other`. |
| `Multiply(immutable(BigInteger)* other)` | Return the exact product. |
| `DivRem(int divisor, ref int remainder)` | Return a fresh quotient and the signed remainder for a 32-bit divisor. |
| `Equals`, `CompareTo`, `GetHashCode` | Value equality, signed ordering, and an equality-compatible hash. |
| `Format(ITextWriter*)` | Write canonical decimal bytes without an intermediate owning string. |
| `ToString()` | Return an independent owning decimal `String`. |

The type implements `IEquatable<BigInteger>`, `IComparable<BigInteger>`,
`IHashable`, `IFormattable`, and `IStringConvertable`, so it works as a hash key,
in generic comparisons, and in interpolation. There are currently no arithmetic
operators, implicit primitive conversions, division by an arbitrary BigInteger, bitwise operations, alternate
radices, or locale formatting.

Parsing accepts an optional leading `+` or `-` followed by at least one ASCII
digit. Whitespace, separators, decimal points, embedded nulls in counted input,
non-ASCII digits, and trailing data are rejected. The complete input is validated
before allocating limb storage. Leading zeroes are accepted; all spellings of zero
share one canonical value. Formatting emits `0` for zero and otherwise an optional
minus sign followed by decimal digits without leading zeroes.

`TryParse` accepts the same grammar as `Parse`. Its result must be an initialized
mutable `BigInteger`, passed by `ref` so successful replacement transfers ownership
without copying limbs. Malformed input returns false and replaces the old result
with zero, releasing its previous storage. A zero counted length fails without
reading input. The null-terminated overload scans to the first NUL, as `Parse`
does; arbitrary-length integers have no fixed text-length limit.

These Try methods are potentially throwing: allocation failure throws
`BadAllocException`, and impossible storage sizes follow the existing size-error
contract. They leave the previous result unchanged on those failures. All syntax
is validated before allocating limbs; malformed input and valid zero spellings
need no limb allocation. A fresh candidate is completely built before the old
result is replaced. No input pointer is retained. Input must remain readable
until that commit; readable input may borrow the old result's limb storage,
which replacement can then release. This does not permit corrupting owner
metadata or passing uninitialized result storage.

```csharp
using std;
int main()
{
    BigInteger value = new(42l);
    if (!BigInteger::TryParse("+00018446744073709551616", ref value)) return 1;
    String canonical = value.ToString();
    if (!canonical.Equals("18446744073709551616")) return 2;
    if (BigInteger::TryParse("12x", ref value) || value.Sign != 0) return 3;
    return 0;
}
```

## Ownership and failure

Ordinary copies allocate independent limb storage. Moving transfers storage
without copying limbs; reinitialize a moved local before reusing it. Arithmetic
does not mutate either input, including when both pointers refer to the same value.
Equal-magnitude cancellation and multiplication by zero return an allocation-free
zero. A failed allocation throws `BadAllocException`; invalid syntax or impossible
storage sizes throw `Exception`. Partial results are cleaned up normally, and
failed result construction leaves its source operands unchanged. The normal value
assignment rules prepare a copied or computed result before replacing the target.

`Format` uses a small stack buffer and borrows limbs while writing. The writer must
consume each byte span during the call, keep the source alive, and must not mutate
or move it through another alias. A writer may allocate or throw. Failure can leave
partial output, while the source integer remains unchanged.

Limbs use base 1,000,000,000, least significant first. Addition and subtraction take
linear time in limb count; schoolbook multiplication takes O(n*m) time and linear
result storage. Parsing and formatting take linear time in decimal digits. The
largest multiplication accumulator is `base*base - 1`, safely within `ulong`.
This initial implementation favors straightforward decimal arithmetic; it does
not use advanced multiplication algorithms.

For explicit source selection include `core/BigInteger.gf`, `Vector`/`Span` and
their query-interface dependencies, `Formatting.gf`, `Hashing.gf`, the implemented
interface sources, and `String`/`ByteSearch`. Projects using the complete standard
library already include these sources.

## Division by a 32-bit integer

`DivRem(divisor, ref remainder)` truncates the quotient toward zero. A nonzero
remainder has the dividend's sign and an absolute value smaller than the absolute
divisor. Negative divisors and `int` minimum are supported. The quotient is a
BigInteger, so dividing a large negative value by -1 has no fixed-width overflow.

A zero divisor throws `Exception` before allocating the quotient or changing the
remainder. Quotient construction completes before the remainder is assigned; a
failed allocation leaves the source, remainder, and an existing assignment target
unchanged. The output integer must not overlap the source object or its owned
storage through unsafe aliases. The quotient owns independent storage.

The method makes one linear pass over base-1,000,000,000 limbs. Its `ulong`
intermediate is at most 2147483647999999999, even for an `int` minimum divisor.
A zero input requires no quotient allocation. A nonzero input can reserve storage
even when its quotient is zero; normalization need not release that capacity.
There is no arbitrary BigInteger-divisor API yet.

For example, repeated division can implement base conversion using existing
String and collection operations; this is an ordinary helper, not a built-in
formatting mode:

```csharp
using std;

String ToRadix(immutable(BigInteger)* input, int radix)
{
    if (radix < 2 || radix > 36) throw new Exception("Radix must be between 2 and 36");
    readonly(char)* digits = "0123456789abcdefghijklmnopqrstuvwxyz";
    BigInteger remaining = *input;
    bool negative = remaining.Sign < 0;
    String text = new();
    while (remaining.Sign != 0)
    {
        int remainder = 0;
        remaining = remaining.DivRem(radix, ref remainder);
        if (remainder < 0) remainder = -remainder;
        text.Append(digits[remainder]);
    }
    if (text.IsEmpty) text.Append('0');
    if (negative) text.Append('-');
    Reverse<char>(text.AsSpan());
    return move text;
}

int main()
{
    BigInteger value = BigInteger::Parse("18446744073709551616");
    String hex = ToRadix(&value, 16);
    if (!hex.Equals("10000000000000000")) return 1;
    return 0;
}
```
