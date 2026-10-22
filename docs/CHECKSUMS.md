# Byte checksums

`std::CRC32` and `std::Adler32` are streaming value types. They process counted
bytes, including embedded zero bytes, without allocation or libc calls. All their
methods and constructors are `nothrow`.

```cpp
std::CRC32 crc = new();
crc.Append("1234", 4);
crc.Append("56789", 5);
uint value = crc.GetValue(); // 0xcbf43926
crc.Reset();
```

Both types expose the same operations:

- Construction and `Reset()` initialize an empty stream.
- `Append(readonly(char)* data, nuint count)` consumes exactly `count` bytes.
  The caller supplies readable storage for those bytes; an empty append does not
  access storage.
- `immutable uint GetValue()` returns the current checksum without resetting it.
  More bytes can be appended afterward.

Copying a checksum copies its independent running state, so a shared prefix can
be continued with different suffixes. Neither type retains the input pointer.

## Algorithms and representation

CRC32 uses the IEEE CRC-32 variant specified for gzip: reflected polynomial
`0xedb88320`, an internal initial state of `0xffffffff`, and a final XOR of
`0xffffffff`. The empty checksum is zero; `"123456789"` yields `0xcbf43926`.
The implementation processes each byte with eight bit steps.
[RFC 1952, CRC sample code](https://www.rfc-editor.org/rfc/rfc1952#section-8)

Adler32 maintains two sums modulo 65521, initially 1 and 0, and returns
`(second << 16) | first`. The empty checksum is one; `"Wikipedia"` yields
`0x11e60398`. Reducing each sum after each byte keeps arithmetic bounded even
for long streams.
[RFC 1950, Adler-32](https://www.rfc-editor.org/rfc/rfc1950#section-2.2)

`GetValue()` returns a number, without choosing a byte order for serialization.
The gzip format stores its CRC32 little endian; the zlib format stores its
Adler32 big endian. These checksums detect accidental byte corruption; they do
not authenticate data.
