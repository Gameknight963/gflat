# Hash file bytes with SHA-256

The optional `libc/File/FileHash.gf` convenience connects binary `File` IO to the
existing [SHA-256 core](SHA256.md). It returns the raw digest as an independent
`byte[32]` value, without allocating an input String, Vector, or digest object.

This complete program prints a file's digest as lowercase hexadecimal:

```csharp
using std;

int main(int argc, char** argv)
{
    if (argc != 2) return 2;
    byte[32] digest = FileHash::SHA256(argv[1]);
    char[64] hex;
    nuint written = 0;
    if (!Hex::TryEncode((readonly(char)*)(byte*)digest, 32, hex, 64, ref written, false))
        return 1;
    Console::WriteLine(hex, written);
    Console::Flush();
    return 0;
}
```

The digest is always 32 bytes; hex encoding produces 64 characters. Neither the
digest nor the local hex buffer needs disposal. The counted output call does not
require a null terminator.

## Whole files and remaining stream bytes

| Call | IO ownership and position |
| --- | --- |
| `FileHash::SHA256(path)` | Opens a binary File, hashes the complete file, and closes it before returning. |
| `FileHash::SHA256(File* input)` | Borrows an open File and hashes from its current position to EOF, using a 4096-byte stack buffer. |
| `FileHash::SHA256(File* input, char* buffer, nuint capacity)` | Same borrowed behavior, using caller-provided scratch storage. |

Borrowed overloads never rewind, close, flush, or clear the File's state. Successful
hashing leaves it open at EOF. Hashing an already exhausted stream returns the
empty-message digest, without recovering its earlier contents. The caller still
owns and closes the File.

The scratch capacity is a byte count, with no terminator reservation. It must be
positive, and the caller must supply that many writable bytes. Its storage must
not overlap the File object. The buffer may be reused immediately after the call;
its contents are scratch data, not an encoded or returned digest. Any positive
chunk size produces the same hash of the same remaining bytes.

An empty file is valid and produces SHA-256's empty-message digest. Embedded NUL,
CR/LF, and non-ASCII values are ordinary bytes; no text decoding, BOM stripping,
line-ending conversion, or newline insertion is performed. The path overload
uses the host's narrow-character file API, like other File conveniences.

## Failures and allocation

A closed File, zero scratch capacity, open/read failure, or owned-file close
failure throws `IOException`. Zero capacity is rejected before consuming input
or touching scratch storage. On a read failure, the borrowed File remains owned
by the caller with its ordinary IO error state. No digest is returned.

The core hash accepts fewer than `2^64` message bits: at most `2^61 - 1` bytes.
Exceeding that limit throws `Exception`. Input may already have advanced by the
offending read; there is no stream-position rollback. IO errors can also consume
an available prefix before throwing.

The path overload closes its owned File during exception unwinding. On the normal
path it explicitly closes before returning the digest, so a close error prevents
success. Borrowed overloads leave all close decisions to their caller.

Normal hashing uses inline SHA state and bounded scratch storage, with no calls
to the application's `Memory::Allocate` for input or digest storage. Opening and
buffering a libc File can allocate inside the host CRT, and throwing exceptions
can allocate too. No file length or snapshot is captured: concurrent file changes
can change the bytes observed during the read.

For explicit source selection, include `File/FileHash.gf`, `File.gf`, `SHA256.gf`,
`Numerics/BitOperations.gf`, and the ordinary File exception/runtime dependencies. The
core `SHA256` remains usable independently of File IO or libc.
