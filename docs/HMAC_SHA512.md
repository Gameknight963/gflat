# HMAC-SHA512 over counted bytes

`Standard::Hashing::HMACSHA512` composes SHA512 with the keyed inner and outer pads
defined by [RFC 2104](https://www.rfc-editor.org/rfc/rfc2104.html#section-2).
It follows the same incremental value API as [HMACSHA256](HMAC_SHA256.md),
returning the complete 64-byte authentication code.

```csharp
using Standard; using Standard::Hashing;

int main()
{
    // RFC 4231 test key and message, for demonstration.
    char[20] key;
    for (nuint i = 0; i < 20; i++) key[i] = (char)0x0b;
    HMACSHA512 authentication = new(key, 20);
    if (!authentication.TryAppend("Hi There", 8)) return 1;
    byte[64] digest = authentication.GetHash();
    if (digest[0] != (byte)0x87 || digest[63] != (byte)0x54) return 2;
    return 0;
}
```

Construction requires an explicit counted key. An empty key uses count zero and
reads no key bytes. Keys longer than SHA512's 128-byte block are first hashed to
64 bytes, then zero-padded to the block size. Shorter keys are copied and padded
directly. The constructor retains no input pointer. Later changes or destruction
of key storage do not affect the initialized value.

`TryAppend(data, count)` consumes counted bytes including NUL and high bytes.
Chunk boundaries do not change the result, and consumed input can be reused.
A zero append reads no input. `GetHash()` is immutable and finalizes a snapshot,
returning an independent `byte[64]` value. Repeated calls agree, and later appends
continue the original message. Copying duplicates the complete keyed state.
Copies can continue with different suffixes. Moving transfers the ordinary value.
`Reset()` starts an empty message with the original key, without reading key
storage again.

SHA512 permits at most `2^125 - 1` input bytes. The inner hash includes its
128-byte pad, leaving `2^125 - 129` bytes for the message. An excessive append
returns false before reading input or changing state. Every representable key
count on supported 32/64-bit pointer widths fits SHA512's length range, so there
is no oversized-key exception analogous to HMACSHA256.

Constructor and methods are `nothrow`, use inline state and bounded stack buffers,
and perform no internal heap allocation. Every accepted nonzero key or message
count requires readable live storage for its complete extent, stable during the
call and disjoint from the receiver state being initialized or modified. The
API supplies no synchronization or concurrent alias-mutation support.

State stores three SHA512 values: preseeded inner and outer hashes and the current
inner hash. Treat these values and their copies as secret-key material. The API
does not promise key erasure, tag verification, key generation, password derivation
or automatic protocol framing. It has no truncation or caller-output-buffer API.

Tests compare all seven [RFC 4231 vectors](https://www.rfc-editor.org/rfc/rfc4231.html#section-4)
and independent .NET HMACSHA512 results, including long keys, binary inputs,
unaligned storage, chunking and independent snapshots. RFC case 5 specifies only
a 128-bit prefix. Tests verify that prefix and compare the full result to .NET.
Seeded private-counter fixtures check overflow preflight.
The shared freestanding utility test is configured for O0/O2 Windows/Linux x64.
Native helper requirements depend on the inspected target and backend.

Explicit source selection needs `core/Hashing/HMACSHA512.gf`,
`core/Hashing/SHA512.gf` and `core/Numerics/BitOperations.gf`. The core has no
Exception, allocator, GC, libc or native-crypto dependency at source level.
