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
| `TryToLong(ref result)` / `TryToULong(ref result)` | Checked 64-bit conversion; false writes zero. |
| `ToLong()` / `ToULong()` | Checked 64-bit conversion; throws when the value does not fit. |
| `Abs()` | Return an independent nonnegative integer with the same magnitude. |
| `Negate()` | Return an independent integer with the opposite sign. |
| `Add(immutable(BigInteger)* other)` | Return the exact sum. |
| `Subtract(immutable(BigInteger)* other)` | Return this value minus `other`. |
| `Multiply(immutable(BigInteger)* other)` | Return the exact product. |
| `Pow(uint exponent)` | Return an independent exact nonnegative integer power; zero to the zero power is one. |
| `DivRem(int divisor, ref int remainder)` | Return a fresh quotient and the signed remainder for a 32-bit divisor. |
| `DivRem(immutable(BigInteger)* divisor, ref BigInteger remainder)` | Return a fresh quotient and replace initialized output with the exact remainder. |
| `Divide(immutable(BigInteger)* divisor)` / `Remainder(immutable(BigInteger)* divisor)` | Return the quotient or remainder of arbitrary-precision division. |
| `Equals`, `CompareTo`, `GetHashCode` | Value equality, signed ordering, and an equality-compatible hash. |
| `Format(ITextWriter*)` | Write canonical decimal bytes without an intermediate owning string. |
| `ToString()` | Return an independent owning decimal `String`. |

The type implements `IEquatable<BigInteger>`, `IComparable<BigInteger>`,
`IHashable`, `IFormattable`, and `IStringConvertable`, so it works as a hash key,
in generic comparisons, and in interpolation. There are currently no arithmetic
operators, implicit primitive conversions, bitwise operations, alternate
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

`Abs()` and `Negate()` borrow the receiver and return independent owners using
the existing value-copy operation. Even a positive value's `Abs()` owns its own
limb storage. Only the copied sign changes; zero stays canonical and copying it
requires no allocation. Both methods accept immutable receivers. Nonzero results
copy limbs in linear time and can throw on allocation failure, leaving the
receiver unchanged. Assignments such as `value = value.Negate()` prepare the
complete result before releasing the old owner. Neither method is `nothrow`.

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

Arbitrary-precision division uses a linear single-limb fast path and normalized
schoolbook long division for larger divisors. For an m-limb dividend and n-limb
divisor with m >= n, it takes O((m-n+1)*n + m+n) work and O(m+n) storage. Its
scratch vectors are reserved once, with no allocation per quotient digit.

For explicit source selection include `core/BigInteger.gf`, `Vector`/`Span` and
their query-interface dependencies, `Formatting.gf`, `Hashing.gf`, the implemented
interface sources, and `String`/`ByteSearch`. Projects using the complete standard
library already include these sources.

## Division and remainder

Both `DivRem` overloads truncate the quotient toward zero. A nonzero remainder
has the dividend's sign, with magnitude less than the divisor's, and satisfies
`dividend = quotient * divisor + remainder`. A zero divisor throws
`Exception("BigInteger divisor is zero")` without changing the remainder output.

The BigInteger-divisor overload borrows its operands and returns an independent
quotient. Its `ref BigInteger remainder` must refer to an initialized mutable
owner. Both new results are complete before that owner is replaced. Allocation
or size failures preserve both operands and the previous remainder value.
Partial results are cleaned up normally.

The output may be the same object as the dividend or divisor, including when
both operands are that object. A successful call then replaces the aliased
operand with the remainder; the returned quotient is independent. All operand
reads finish before this commit. This permits ordinary complete owning objects,
not fabricated output references inside owner metadata or limb storage.

```csharp
using std;
int main()
{
    BigInteger dividend = BigInteger::Parse("-100000000000000000001");
    BigInteger divisor = BigInteger::Parse("10000000001");
    BigInteger remainder = new(42l);
    BigInteger quotient = dividend.DivRem(&divisor, ref remainder);
    String q = quotient.ToString();
    String r = remainder.ToString();
    if (!q.Equals("-9999999999") || !r.Equals("-2")) return 1;
    return 0;
}
```

`Divide` and `Remainder` return the corresponding independent value with the
same rules. They share `DivRem`'s algorithm and currently compute both results
before discarding the unused one. Division adds no GC, compiler arithmetic
support, or external dependency. Like the other arithmetic, it is not constant
time and makes no cryptographic timing guarantee.

## Checked primitive conversions

Immutable `TryToLong(ref long result)` accepts the entire signed 64-bit range,
including `long` minimum. `TryToULong(ref ulong result)` accepts zero through
`ulong` maximum and rejects negative values. Both are `nothrow`, allocate nothing,
and write zero to the output on failure. A failed conversion does not wrap,
truncate, or preserve the previous output value. The receiver is unchanged.

Results are staged before assignment. Output metadata must be separate from
both the receiver object's storage and its owned limb allocation. These methods
borrow the integer; they do not transfer ownership or allocate a decimal string.

`ToLong()` and `ToULong()` return the same checked results or throw
`Exception("BigInteger does not fit in long")` or
`Exception("BigInteger does not fit in ulong")`, respectively. Successful calls
allocate nothing; errors follow normal exception allocation rules. These named
methods provide checked range conversion, independently of primitive casts'
ordinary bit-truncation rules. No conversion operator or implicit conversion is
introduced.

```csharp
using std;
int main()
{
    BigInteger integer = BigInteger::Parse("18446744073709551615");
    ulong unsignedValue = 0ul;
    if (!integer.TryToULong(ref unsignedValue) || unsignedValue != 18446744073709551615ul) return 1;
    long signedValue = 42l;
    if (integer.TryToLong(ref signedValue) || signedValue != 0l) return 2;
    return 0;
}
```

## Nonnegative integer powers

Immutable `Pow(uint exponent)` returns a fresh independent integer and leaves
the receiver unchanged. The exponent is nonnegative; `Pow(0)` returns one for
every base, including zero. Zero to a positive exponent is zero. Negative bases
produce a negative result for odd exponents and a positive result for even ones.
`Pow(1)` copies the receiver without sharing its limb allocation.

The method uses left-to-right exponentiation by squaring through the existing
`Multiply` operation, requiring O(log exponent) multiplications. This does not
bound the size of the result: large powers can require substantial storage.
Zero to a positive exponent allocates nothing. Results equal to one or minus one
still require their own limb storage; those bases use a direct fast path even
for the largest `uint` exponent. Allocation or size failures propagate normally,
clean up intermediate owners, and preserve the receiver and any existing
assignment target.

```csharp
using std;
int main()
{
    BigInteger baseValue = new(-12l);
    BigInteger power = baseValue.Pow(5u);
    String canonical = power.ToString();
    if (!canonical.Equals("-248832") || baseValue.Sign != -1) return 1;
    return 0;
}
```

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
