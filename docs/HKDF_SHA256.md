# HKDF-SHA256 key extraction and expansion

`Standard::Hashing::HKDFSHA256` composes the existing HMACSHA256 core according to
[RFC 5869](https://www.rfc-editor.org/rfc/rfc5869.html#section-2). It separates
extracting a 32-byte pseudorandom key from expanding that key into caller storage.
It is intended for suitable key material, not password hashing; HKDF does not
provide the deliberate slowdown used against password guessing.

```csharp
using Standard; using Standard::Hashing;

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

`Extract(salt, saltCount, input, inputCount)` reads counted bytes and returns an
independent ordinary `byte[32]` value. Empty salt is count zero and produces the
same padded HMAC key as RFC 5869's default 32 zero bytes. Zero counts do not read
their corresponding pointers. Input and salt can overlap each other as readable,
stable storage. No pointer is retained; input storage can be reused after return.

SHA256 permits at most `2^61 - 1` bytes per hash; HMAC reserves 64 bytes for its
inner pad. Extract therefore rejects salt above `2^61 - 1` bytes or input above
`2^61 - 65` bytes, throwing `Exception` before reading either input. Successful
calls allocate no heap storage. Ordinary thrown exceptions may allocate.

`TryExpand(prk, prkCount, info, infoCount, output, outputLength)` is `nothrow`.
The last argument is both the requested count and the caller's declared writable
extent. Success writes exactly that many raw bytes, leaving following storage
untouched. It does not receive a separate capacity or report a partial count;
the caller must actually provide the declared writable region.

Expansion accepts output lengths 1 through 8160, the RFC limit of 255 SHA256
blocks. Zero output returns false. PRK must contain 32 through `2^61 - 1` bytes.
For a one-block output of at most 32 bytes, info can contain at most `2^61 - 66`
bytes. Longer output permits at most `2^61 - 98` info bytes, leaving room for
the previous 32-byte block and one counter byte in each HMAC input.

Invalid lengths or output/info overlap return false before reading any input or
writing output. All accepted input regions must be readable and stable while
needed, and output must be writable for the complete requested count. Non-null
pointer arguments remain required by the language even for zero-count salt/info.
Neither method allocates internally on success, validates arbitrary storage
ownership or supports concurrent alias mutation.

Output may overlap PRK, including at a different offset: the complete key is
consumed into private HMAC state before output starts, and expansion does not
reread it. Output must not overlap nonempty info, which is read again for each
block. That overlap is rejected even for a one-block request. Only the actual
output interval is checked; adjacent info in the same allocation is permitted.
PRK and info can overlap as input, provided output remains disjoint from info.
Successful writes naturally replace any PRK bytes overlapping output.

Tests check the three RFC SHA256 vectors and independent .NET HKDF results,
binary/unaligned input, block boundaries and the 255th block, output guards,
permitted key overlap, rejected info overlap and inaccessible-pointer preflight.
Huge valid input lengths are documented arithmetic limits, not enormous messages
executed by the tests. There is no key-erasure or password-derivation promise.

Explicit sources require `core/Hashing/HKDFSHA256.gf`, HMACSHA256, SHA256,
BitOperations and ordinary Exception support. There is no native-crypto, File IO,
GC, owning String or new interface dependency.
