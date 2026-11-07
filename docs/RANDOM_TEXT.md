# Generating random text

Load `Standard/core/Numerics/pseudo_random_text.gf` for `GetString` and
`GetHexString` on `PseudoRandom`. Freestanding forms take `ref PseudoRandom`
as their first argument.

```csharp
using Standard::Text;
using Standard::Numerics;

int main()
{
    PseudoRandom random = new(42ul);
    String label = random.GetString("ABCDEFGHJKLMNPQRSTUVWXYZ23456789", 12);
    String hex = random.GetHexString(16, true);
    if (label.Length != 12 || hex.Length != 16) return 1;
    return 0;
}
```

`GetString(choices, length)` returns an owning string of the requested byte
length, sampling choices with replacement. Choices can be a zero-terminated
`readonly(char)*` or a counted `Span<readonly(char)>`. The span form includes
embedded zero bytes. Choices must be nonempty even for a zero-length result.
Each byte consumes one unbiased bounded `NextULong` selection. Sampling an
alphabet of UTF-8 bytes can produce invalid UTF-8. Use an ASCII alphabet for
text that is always valid UTF-8.

`GetHexString(length, lowercase = false)` returns an owning string of that many
hexadecimal characters. The length is a character count, not a number of
encoded random bytes. A second overload fills a `Span<char>` without adding
a terminator. Both choose one hexadecimal digit per bounded draw. Uppercase
and lowercase calls consume identical draws for the same seed and length.

Zero-length results consume no random words or allocation. String allocation
occurs before drawing, so allocation failure preserves the generator and an
assignment destination. Span filling does not allocate. Owning results include
the ordinary trailing string terminator in addition to their counted bytes.

Results are deterministic for the seed and calls made, but are not suitable
for passwords, authentication tokens, or other cryptographic uses.
