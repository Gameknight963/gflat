# HKDF-SHA256 key extraction and expansion

`Standard::Hashing::HKDFSHA256` derives keys using
[RFC 5869](https://www.rfc-editor.org/rfc/rfc5869.html#section-2).
Extraction produces a 32-byte pseudorandom key. Expansion produces the requested
key bytes from that key and application-specific info.

```csharp
using Standard;
using Standard::Hashing;

int main()
{
    // RFC 5869 test material, for demonstration.
    char[22] input;
    for (nuint i = 0; i < 22; i++) input[i] = (char)0x0b;
    char[13] salt;
    for (nuint i = 0; i < 13; i++) salt[i] = (char)i;
    char[10] info;
    for (nuint i = 0; i < 10; i++) info[i] = (char)(240 + i);
    byte[32] prk = HKDFSHA256::Extract(salt, 13, input, 22);
    byte[42] key;
    if (!HKDFSHA256::TryExpand(((byte*)prk).reinterpret<readonly(char)*>(), 32, info, 10, key, 42)) return 1;
    if (key[0] != (byte)0x3c || key[41] != (byte)0x65) return 2;
    return 0;
}
```

## Extract

`Extract(salt, saltCount, input, inputCount)` returns `byte[32]`. An empty salt
has the RFC's default effect of 32 zero bytes. Input and salt may overlap.

The maximum salt count is `2^61 - 1`. The maximum input count is `2^61 - 65`.
Exceeding either throws `Exception` before reading the inputs.

## Expand

`TryExpand(prk, prkCount, info, infoCount, output, outputLength)` fills exactly
the requested output extent.

| Parameter | Accepted size |
| --- | --- |
| `prkCount` | 32 through `2^61 - 1` bytes |
| `outputLength` | 1–8160 bytes, up to 255 SHA-256 blocks |
| `infoCount`, output up to 32 bytes | At most `2^61 - 66` bytes |
| `infoCount`, longer output | At most `2^61 - 98` bytes |

Invalid lengths return false and preserve output. Output may overlap PRK,
which is read into keyed state before writing. Output overlap with nonempty
info is rejected because expansion rereads info for each block. Adjacent regions
within one allocation are allowed.

HKDF is intended for key material. Password-derived keys need a password KDF
with an appropriate work factor. Derived and intermediate keys occupy ordinary
storage until overwritten.

Sources: `core/Hashing/HKDFSHA256.gf`, `HMACSHA256.gf`, and their hash dependencies.
