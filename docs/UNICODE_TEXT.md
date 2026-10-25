# Owning Unicode conversions

The optional `std/core/UnicodeText.gf` source adds two counted conversions:

```csharp
String UnicodeText::FromUtf16(readonly(ushort)* input, nuint units);
Vector<ushort> UnicodeText::ToUtf16(readonly(char)* input, nuint bytes);
```

`FromUtf16` returns an independent, null-terminated UTF-8 String. `ToUtf16` returns
independent UTF-16 code units in a Vector. Its `Count` includes only the converted
code units: **no terminator is appended**. Counts are bytes for UTF-8 and ushort
code units for UTF-16, never scalar counts or byte counts of the ushort storage.
Embedded zeros are ordinary counted data in both directions. A BOM code point
is preserved, not inserted or stripped. No normalization or newline conversion
is performed.

Both operations strictly validate the entire input and compute its exact output
length before allocating storage. Invalid UTF-8, overlong sequences, surrogate
scalar encodings, or isolated/truncated UTF-16 surrogates throw `Exception`.
Length overflow throws rather than wrapping. No replacement characters are
silently substituted. Output allocation failure throws `BadAllocException`.
Empty input accesses no input bytes and allocates no output storage; a nonempty
result reserves exactly once. Inputs remain unchanged on success or failure.

```csharp
using std; using std::Collections;
ushort[3] source = [(ushort)'A', (ushort)0xd83d, (ushort)0xde00];
String utf8 = UnicodeText::FromUtf16(source, 3);
Vector<ushort> utf16 = UnicodeText::ToUtf16(utf8.Data, utf8.Length);
```

The caller keeps the input alive and unchanged for the whole call; results no
longer borrow input afterward. The algorithms reuse `UnicodeTranscoding`, `Utf8`,
and `Utf16` with small stack chunks. They do not cast away String protection or
write into uninitialized Vector elements.

UTF-16 input/output consists of host `ushort` values, not an endian-specific byte
stream. Serialized UTF-16 bytes need an explicit endian decoder. This API does
not assume that a platform's `wchar_t` is 16 bits. For a native API explicitly
requiring null-terminated UTF-16 units, append `(ushort)0` to the returned Vector,
then borrow its data while the owner remains alive and unmodified. Embedded zeros
still terminate such native calls early; use counted native APIs when necessary.

Explicit source selections need `UnicodeText.gf`, `UnicodeTranscoding.gf`,
`Utf8.gf`, `Utf16.gf`, String and Vector with their ordinary dependencies. The
complete standard library includes them. The caller-buffer codecs remain usable
without these owning conveniences.
