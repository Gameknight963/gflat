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
| `Sign` | -1, 0, or 1. |
| `Add(immutable(BigInteger)* other)` | Return the exact sum. |
| `Subtract(immutable(BigInteger)* other)` | Return this value minus `other`. |
| `Multiply(immutable(BigInteger)* other)` | Return the exact product. |
| `Equals`, `CompareTo`, `GetHashCode` | Value equality, signed ordering, and an equality-compatible hash. |
| `Format(ITextWriter*)` | Write canonical decimal bytes without an intermediate owning string. |
| `ToString()` | Return an independent owning decimal `String`. |

The type implements `IEquatable<BigInteger>`, `IComparable<BigInteger>`,
`IHashable`, `IFormattable`, and `IStringConvertable`, so it works as a hash key,
in generic comparisons, and in interpolation. There are currently no arithmetic
operators, implicit primitive conversions, division, bitwise operations, alternate
radices, locale formatting, or `TryParse` API.

Parsing accepts an optional leading `+` or `-` followed by at least one ASCII
digit. Whitespace, separators, decimal points, embedded nulls in counted input,
non-ASCII digits, and trailing data are rejected. The complete input is validated
before allocating limb storage. Leading zeroes are accepted; all spellings of zero
share one canonical value. Formatting emits `0` for zero and otherwise an optional
minus sign followed by decimal digits without leading zeroes.

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
