# Byte checksums

`Standard::Hashing` provides three incremental checksum values:

| Type | Algorithm | Empty checksum | Check value |
| --- | --- | --- | --- |
| `CRC32` | IEEE, reflected polynomial `0xedb88320` | 0 | `"123456789"` → `0xcbf43926` |
| `CRC32C` | Castagnoli, reflected polynomial `0x82f63b78` | 0 | `"123456789"` → `0xe3069283` |
| `Adler32` | Two sums modulo 65521 | 1 | `"Wikipedia"` → `0x11e60398` |

```cpp
using Standard::Hashing;

CRC32 crc = new();
crc.Append("1234", 4);
crc.Append("56789", 5);
uint value = crc.GetValue(); // 0xcbf43926
crc.Reset();
```

All three expose `Append(data, count)`, `GetValue()`, and `Reset()`. Getting the
current value preserves the running message. Copies can continue a shared prefix
with different suffixes.

Append input can overlap the checksum's initialized storage. The running state
is stored after reading the input.

## Algorithms and representation

Both CRC types initialize state to `0xffffffff` and XOR the final state with
`0xffffffff`. Choose the variant required by the file or protocol format:
CRC32 and CRC32C produce different results.

```csharp
using Standard;
using Standard::Hashing;

int main()
{
    CRC32C checksum = new();
    checksum.Append("1234", 4);
    checksum.Append("56789", 5);
    if (checksum.GetValue() != 0xe3069283u) return 1;
    CRC32C saved = checksum;
    checksum.Reset();
    if (checksum.GetValue() != 0u || saved.GetValue() != 0xe3069283u) return 2;
    return 0;
}
```

Adler32 starts its sums at 1 and 0 and returns `(second << 16) | first`.

`GetValue()` returns a number. Serialize it in the byte order required by the
format: gzip stores CRC32 little endian, while zlib stores Adler32 big endian.
These checksums detect accidental corruption. For keyed authentication, use
[HMAC](HMAC_SHA256.md).

Algorithm references: [CRC32](https://www.rfc-editor.org/rfc/rfc1952#section-8),
[CRC32C](https://www.rfc-editor.org/rfc/rfc3720#section-12.1),
[Adler32](https://www.rfc-editor.org/rfc/rfc1950#section-2.2).

Sources: `core/Hashing/CRC32.gf`, `CRC32C.gf`, and `Adler32.gf`
