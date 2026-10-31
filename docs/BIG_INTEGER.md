# BigInteger

`Standard::Numerics::BigInteger` represents signed integers whose precision is
limited by available storage. For example, compute 100 factorial:

```csharp
using Standard;
using Standard::Numerics;

int main()
{
    BigInteger factorial = new BigInteger(1);
    for (long factor = 2l; factor <= 100l; factor++)
    {
        BigInteger operand = new BigInteger(factor);
        factorial = factorial.Multiply(&operand);
    }
    char[256] buffer;
    nuint written = 0;
    if (!factorial.TryFormat(buffer, 256, ref written)) return 1;
    Console::WriteLine(buffer, written);
    Console::Flush();
    return 0;
}
```

Arithmetic methods borrow their operands and return owning results. Copies
have independent limb storage, while `move` transfers it. Zero needs no limb
allocation. The type supports equality, ordering, hashing, and interpolation.

## Construction and parsing

| API | Result |
| --- | --- |
| `new BigInteger()` | Zero |
| `new BigInteger(long)` | Exact signed 64-bit value |
| `BigInteger::FromULong(value)` | Exact unsigned 64-bit value |
| `Parse(text)` / `Parse(data, count)` | Parsed decimal value, or an exception for invalid text |
| `TryParse(text, ref result)` / `TryParse(data, count, ref result)` | True with the parsed value, or false with `result` reset to zero |

Input uses an optional `+` or `-` followed by ASCII decimal digits. Leading
zeroes are accepted. Whitespace, separators, radix prefixes, decimal points,
and counted embedded NULs are rejected.

TryParse validates syntax before allocating limbs. Allocation failure throws
`BadAllocException` and preserves the previous result. This differs from
malformed input, which replaces it with zero. Readable input can borrow the
result's old storage because replacement happens after parsing.

```csharp
using Standard::Text;
using Standard;
using Standard::Numerics;

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

## Arithmetic

| API | Result |
| --- | --- |
| `Add(&other)` | Exact sum |
| `Subtract(&other)` | This value minus the other |
| `Multiply(&other)` | Exact product |
| `Abs()` / `Negate()` | Absolute value or opposite sign |
| `GreatestCommonDivisor(&first, &second)` | Nonnegative GCD, with `gcd(0, 0) == 0` |
| `LeastCommonMultiple(&first, &second)` | Nonnegative LCM, or zero if either input is zero |

GCD and LCM ignore operand signs. LCM divides out the GCD before multiplying
to reduce intermediate size. Arithmetic can fail on allocation or storage-size
limits. Operands remain available when result construction fails.

## Division and remainder

`DivRem(&divisor, ref remainder)` returns the quotient and replaces the
remainder. The quotient truncates toward zero. A nonzero remainder has the
dividend's sign and a smaller magnitude than the divisor.

A zero divisor throws `Exception("BigInteger divisor is zero")`. Both results
are prepared before replacing the remainder, so allocation failures preserve
its previous value. The remainder may alias either operand. On success that
operand is replaced with the remainder.

```csharp
using Standard::Text;
using Standard;
using Standard::Numerics;

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

`Divide(&divisor)` and `Remainder(&divisor)` return the corresponding value.
They currently compute both results and discard the unused one.

## Primitive conversions

`TryToLong(ref result)` and `TryToULong(ref result)` return false and write
zero when out of range. Both are `nothrow`. `ToLong()` and `ToULong()` throw
instead. The signed form accepts the entire `long` range, and the unsigned
form rejects negative values.

```csharp
using Standard;
using Standard::Numerics;

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

Keep primitive output storage separate from the integer and its owned limbs.

## Integer powers

`Pow(uint exponent)` uses exponentiation by squaring. `Pow(0)` returns one,
including for zero, and negative bases produce negative results for odd powers.
Large powers can require substantial storage despite logarithmically many
multiplications.

```csharp
using Standard::Text;
using Standard;
using Standard::Numerics;

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

`DivRem(int divisor, ref int remainder)` follows the same truncation and
remainder-sign rules. Negative divisors and `int` minimum are supported.
A zero divisor or failed quotient construction preserves the old remainder.
Its output slot must be separate from the BigInteger's storage.

Repeated division can convert to another radix:

```csharp
using Standard::Text;
using Standard;
using Standard::Numerics;
using Standard::Collections;

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

## Modular exponentiation

`ModPow(&exponent, &modulus)` computes a power's signed remainder while reducing
intermediate products. The exponent must be nonnegative and modulus nonzero.
If both are invalid, the negative exponent is reported first.

Negative moduli behave like their positive magnitude. A negative base raised
to an odd exponent can return a negative remainder. Modulus `1` or `-1`
produces zero. These rules match [.NET BigInteger.ModPow](https://learn.microsoft.com/en-us/dotnet/api/system.numerics.biginteger.modpow).

```csharp
using Standard;
using Standard::Numerics;

int main()
{
    BigInteger value = new(4l);
    BigInteger exponent = new(13l);
    BigInteger modulus = new(497l);
    BigInteger result = value.ModPow(&exponent, &modulus);
    long small = 0l;
    if (!result.TryToLong(ref small) || small != 445l) return 1;
    return 0;
}
```

The implementation uses repeated squaring and decimal-limb division. It is
variable-time and unsuitable for operations requiring cryptographic timing
guarantees.

## Formatting

Formatting uses canonical decimal digits, with `0` for zero and a minus sign
for negative values.

- `ToString()` returns an owning String
- `Format(writer)` streams digits to a writer
- `TryFormat(output, capacity, ref written)` writes counted bytes without a
  terminator. Insufficient capacity returns false, resets `written` to zero,
  and preserves output

TryFormat is `nothrow`. Its output and `written` must be separate from each
other, the receiver, and its owned limbs. A writer must consume bytes during
each call and keep the integer alive and stable. Writer errors can leave
partial output.

## Costs

The implementation stores base-1,000,000,000 limbs. Addition, subtraction,
parsing, and formatting are linear in their input sizes. Multiplication uses
O(n*m) work. Multi-limb long division uses
O((m-n+1)*n + m+n) work for an m-limb dividend and n-limb divisor.

Source: `core/Numerics/BigInteger.gf`, with Vector, String, and interface
dependencies.
