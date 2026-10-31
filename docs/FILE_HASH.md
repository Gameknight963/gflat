# Hash file bytes with SHA-256 or SHA-512

`Standard::IO::FileHash` hashes whole files or remaining stream bytes:

```csharp
using Standard;
using Standard::IO;
using Standard::Encoding;
using Standard::Hashing;

int main(int argc, char** argv)
{
    if (argc != 2) return 2;
    byte[32] digest = FileHash::SHA256(argv[1]);
    char[64] hex;
    nuint written = 0;
    if (!Hex::TryEncode(((byte*)digest).reinterpret<readonly(char)*>(), 32, hex, 64, ref written, false))
        return 1;
    Console::WriteLine(hex, written);
    Console::Flush();
    return 0;
}
```

| Overload | Position and ownership |
| --- | --- |
| `SHA256(path)` | Opens, hashes, and closes the complete file |
| `SHA256(File* input)` | Hashes from current position to EOF, leaving the File open |
| `SHA256(File* input, buffer, capacity)` | Same operation with caller scratch |

Matching `SHA512` overloads return `byte[64]` instead of `byte[32]`. Hexadecimal
output needs twice the digest size: 64 characters for SHA-256 or 128 for SHA-512.

## Whole files and remaining stream bytes

Borrowed overloads leave the File at EOF. An already exhausted File produces the
empty-message digest. Scratch capacity must be positive, and scratch must be
separate from the File object. Changing a file concurrently can change the bytes
observed during hashing.

## Failures and allocation

Opening, reading, closing, a closed File, or zero scratch capacity can throw
`IOException`. A read failure may leave input consumed. The path overload closes
its owned File while unwinding. A normal close failure prevents returning a digest.

Messages exceeding the SHA algorithm's length limit throw `Exception`.

Source: `libc/IO/FileHash.gf`
