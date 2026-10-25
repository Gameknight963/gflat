# HMAC-SHA256 over counted bytes

`std::Hashing::HMACSHA256` provides keyed message authentication using HMAC and SHA-256.
It composes the existing SHA256 implementation with the inner/outer pads defined
by [RFC 2104](https://www.rfc-editor.org/rfc/rfc2104).

```csharp
using std; using std::Hashing;

int main()
{
    // Example input only: choose a secret key appropriate for your protocol.
    HMACSHA256 authentication = new("secret", 6);
    authentication.TryAppend("hello", 5);
    byte[32] digest = authentication.GetHash();
    char[64] hex;
    nuint written = 0;
    Hex::TryEncode((readonly(char)*)(byte*)digest, 32, hex, 64, ref written, false);
    Console::WriteLine(hex, written);
    Console::Flush();
    return 0;
}
```

## Operations and limits

- `new HMACSHA256(key, count)` requires an explicit counted key. There is no
  parameterless constructor. Empty keys are representable by explicitly passing
  count zero. Keys longer than 64 bytes are hashed before applying HMAC pads.
  The constructor reads all key bytes and retains no input pointer. Later key
  mutation/destruction does not affect the value.
- `TryAppend(data, count)` is `nothrow`. It appends counted bytes, including NUL
  and arbitrary high bytes, and returns false before any input read/state change
  when the complete append would exceed the supported message length.
- `GetHash()` is an `immutable`, `nothrow` operation returning an independent
  fixed `byte[32]` result. It finalizes copied state; repeated calls agree and
  later appends continue the same message.
- `Reset()` is `nothrow` and starts a new empty message with the same key.

SHA-256 permits at most `2^61 - 1` bytes per hash. HMAC reserves 64 bytes for the
inner pad, so a message can contain at most `2^61 - 65` bytes. An impossible key
length greater than `2^61 - 1` throws `Exception` before reading key storage.
A zero-length key or append does not read its corresponding input pointer.
Other nonempty inputs must designate valid storage for their complete count.

The value stores three inline SHA states: preseeded inner and outer hashes and
the current inner hash. Copying creates independent keyed state. Moving transfers
it as an ordinary value. Successful operations do not call the application
allocator, libc, or the GC. Throwing from construction can allocate an exception.

Input storage must remain valid and unchanged during each call, and must not
overlap state being modified by that call. This API does not promise to erase
keys or intermediate state from registers, stack slots, copies, or memory on
destruction. It supplies no random key generation, tag verification, password
derivation, encryption, file policy, or automatic protocol framing. Treat the
stored state as secret-key material.

Tests verify [RFC 4231](https://www.rfc-editor.org/rfc/rfc4231) vectors and
the independent .NET HMAC implementation, including keys longer than one SHA
block. RFC 4231 case 5 specifies only the first 128 output bits; the library
always returns all 256 bits and has no truncation option.

Explicit source selection needs `core/Hashing/HMACSHA256.gf`, `core/Hashing/SHA256.gf`,
`core/Numerics/BitOperations.gf`, and ordinary exception support for invalid key lengths.
