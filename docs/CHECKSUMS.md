# Byte checksums

`std::CRC32`, `std::CRC32C`, and `std::Adler32` are streaming value types. They
process counted bytes, including embedded zero bytes, without allocation or libc
calls. All their methods and constructors are `nothrow`.

```cpp
std::CRC32 crc = new();
crc.Append("1234", 4);
crc.Append("56789", 5);
uint value = crc.GetValue(); // 0xcbf43926
crc.Reset();
```

All three types expose the same operations:

- Construction and `Reset()` initialize an empty stream.
- `Append(readonly(char)* data, nuint count)` consumes exactly `count` bytes.
  The caller supplies readable storage for those bytes; an empty append does not
  access storage.
- `immutable uint GetValue()` returns the current checksum without resetting it.
  More bytes can be appended afterward.

Copying a checksum copies its independent running state, so a shared prefix can
be continued with different suffixes. None of these types retains the input pointer.

Append stages its numeric running state in local integers and commits it only
after consuming the input. Counted input may therefore overlap the checksum's
own initialized storage: its bytes are read before that final state update.
This does not allow concurrent mutation of input or simultaneous mutation of the
same checksum object.

## Algorithms and representation

CRC32 uses the IEEE CRC-32 variant specified for gzip: reflected polynomial
`0xedb88320`, an internal initial state of `0xffffffff`, and a final XOR of
`0xffffffff`. The empty checksum is zero; `"123456789"` yields `0xcbf43926`.
The implementation processes each byte with eight bit steps.
[RFC 1952, CRC sample code](https://www.rfc-editor.org/rfc/rfc1952#section-8)

CRC32C uses the Castagnoli polynomial, distinct from CRC32's IEEE polynomial.
Its forward generator is `0x11edc6f41`, represented by reflected low-word
`0x82f63b78` in the implementation. Internal initial state and final XOR are both
`0xffffffff`; the empty result is zero and `"123456789"` yields `0xe3069283`.
It processes each byte with eight bit steps, without tables or CPU-specific
instructions. CRC32C is useful when a binary format requires that particular
checksum; it cannot substitute for CRC32 in a format requiring the IEEE variant.
[RFC 3720, digest definition](https://www.rfc-editor.org/rfc/rfc3720#section-12.1)
and [reference vectors](https://www.rfc-editor.org/rfc/rfc3720#appendix-B.4).

The RFC lists serialized digest bytes; `GetValue()` returns the numeric CRC,
without choosing a serialization byte order. For example, the RFC's zero-filled
32-byte vector lists `aa 36 91 8a`; its numeric CRC32C is `0x8a9136aa`.

```csharp
using std;
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

Adler32 maintains two sums modulo 65521, initially 1 and 0, and returns
`(second << 16) | first`. The empty checksum is one; `"Wikipedia"` yields
`0x11e60398`. Reducing each sum after each byte keeps arithmetic bounded even
for long streams.
[RFC 1950, Adler-32](https://www.rfc-editor.org/rfc/rfc1950#section-2.2)

`GetValue()` returns a number, without choosing a byte order for serialization.
The gzip format stores its CRC32 little endian; the zlib format stores its
Adler32 big endian. These checksums detect accidental byte corruption; they do
not authenticate data.

For SHA-256 file/content digests with copied fixed-buffer results, see
[`SHA256`](SHA256.md).
