# HMAC-SHA256 over counted bytes

`Standard::Hashing::HMACSHA256` computes a keyed message authentication code
according to [RFC 2104](https://www.rfc-editor.org/rfc/rfc2104).

```csharp
using Standard;
using Standard::Encoding;
using Standard::Hashing;

int main()
{
    // Example input only: choose a secret key appropriate for your protocol.
    HMACSHA256 authentication = new("secret", 6);
    authentication.TryAppend("hello", 5);
    byte[32] digest = authentication.GetHash();
    char[64] hex;
    nuint written = 0;
    Hex::TryEncode(((byte*)digest).reinterpret<readonly(char)*>(), 32, hex, 64, ref written, false);
    Console::WriteLine(hex, written);
    Console::Flush();
    return 0;
}
```

## Operations and limits

| Operation | Effect |
| --- | --- |
| `new HMACSHA256(key, count)` | Initializes from a counted key |
| `TryAppend(data, count)` | Adds message bytes |
| `GetHash()` | Returns a `byte[32]` tag of the current message |
| `Reset()` | Starts an empty message with the same key |

Construction reads the complete key, hashing it first when longer than 64 bytes.
The original key buffer can then be reused. `GetHash` finalizes a snapshot.
Copies of the authentication value can continue with independent suffixes.

Keys longer than `2^61 - 1` bytes throw `Exception`. Messages can contain at most
`2^61 - 65` bytes because the inner pad occupies 64 hash-input bytes. An excessive
append returns false and preserves state. Keep input separate from mutable state.

The value and its copies contain secret-derived state. Treat them as key material.
State remains in ordinary value storage until overwritten. The returned tag is
the complete 32-byte HMAC.

Sources: `core/Hashing/HMACSHA256.gf`, `SHA256.gf`, and `Numerics/BitOperations.gf`
