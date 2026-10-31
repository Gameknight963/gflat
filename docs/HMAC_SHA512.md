# HMAC-SHA512 over counted bytes

`Standard::Hashing::HMACSHA512` implements
[RFC 2104](https://www.rfc-editor.org/rfc/rfc2104.html#section-2) with SHA-512
and returns a complete 64-byte authentication tag.

```csharp
using Standard;
using Standard::Hashing;

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

| Operation | Effect |
| --- | --- |
| `new HMACSHA512(key, count)` | Initializes from a counted key |
| `TryAppend(data, count)` | Adds message bytes |
| `GetHash()` | Returns a `byte[64]` tag of the current message |
| `Reset()` | Starts an empty message with the same key |

Keys longer than 128 bytes are hashed first. Construction reads the complete key,
so its original buffer can then be reused. `GetHash` finalizes a snapshot.
Copies can continue with independent suffixes.

The message limit is `2^125 - 129` bytes, allowing for the inner pad. An excessive
append returns false and preserves state. Keep input separate from mutable state.

The value and its copies contain secret-derived state. Treat them as key material.
State remains in ordinary value storage until overwritten.

Sources: `core/Hashing/HMACSHA512.gf`, `SHA512.gf`, and `Numerics/BitOperations.gf`
