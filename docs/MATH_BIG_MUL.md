# Full-width integer multiplication

`Math::BigMul` retains the full product instead of truncating it to the input
width. Load `Standard/core/Numerics/Math.gf`.

| Arguments | Result |
| --- | --- |
| `int, int` | Exact `long` product |
| `uint, uint` | Exact `ulong` product |
| `long, long` | Exact `extralong` product |
| `long, long, ref long low` | Signed high 64 bits, with low 64 bits written through `low` |
| `ulong, ulong, ref ulong low` | Unsigned high 64 bits, with low 64 bits written through `low` |

```csharp
using Standard::Numerics;

int main()
{
    if (Math::BigMul(2147483647, 2) != 4294967294l) return 1;
    ulong low = 0ul;
    ulong high = Math::BigMul(18446744073709551615ul, 2ul, ref low);
    if (high != 1ul || low != 18446744073709551614ul) return 2;
    extralong exact = Math::BigMul(-9223372036854775807l - 1l, -1l);
    if (exact != (extralong)9223372036854775807l + (extralong)1) return 3;
    return 0;
}
```

The split signed form exposes the two's-complement representation of the
128-bit result. The low word can therefore be negative independently of the
product's sign. Inputs are passed by value, so the low output may reuse an
input variable. Both words represent the complete product for every input.

There is no unsigned 128-bit scalar type, so use the split form for unsigned
64-bit multiplication. All overloads are `nothrow`, do not allocate, and make
no libc calls.
