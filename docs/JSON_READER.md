# JSON reader

`std::JsonReader` reads one complete JSON document from a borrowed counted UTF-8
buffer. It emits tokens without allocating strings, converting numbers, or building
a DOM. Its `Vector<byte>` nesting stack grows as needed; there is no recursive
parser call stack or fixed nesting limit. Allocation can throw `BadAllocException`.

```cpp
using std;

readonly(char)* document = "{\"ready\":true}";
JsonReader reader = new(document, 14);
while (reader.Read()) {
    JsonTokenKind kind = reader.Kind;
    readonly(char)* raw = document + reader.TokenOffset;
    nuint count = reader.TokenLength;
    // Process the counted raw lexeme here.
}
```

`Read()` returns `true` for a token. `false` means exactly one root value and all
trailing JSON whitespace have been consumed and validated. Calling it again after
completion returns `false`. Stopping early does **not** validate the remaining
document. Empty input, incomplete values, and trailing non-whitespace are errors.

`JsonTokenKind` contains `StartObject`, `EndObject`, `StartArray`, `EndArray`,
`PropertyName`, `String`, `Number`, `True`, `False`, `Null`, and `None`. Commas and
colons belong to the grammar and are not emitted. `TokenOffset` and `TokenLength`
identify exact byte ranges in the original buffer, including surrounding quotes
for property names and strings. Number spelling is preserved, including exponent,
sign, and trailing fractional zeros. Large numbers have no machine numeric range
restriction. `Depth` is the number of enclosing containers: a root opener and its
closing delimiter both have depth zero; its immediate contents have depth one.
`Kind` becomes `None` and `TokenLength` becomes zero on completion or error.

Pass a string token's raw range to `JsonString::TryGetUnquotedLength` and
`JsonString::TryUnquote` to decode it into caller storage. The reader already
validates every quoted token through that same helper. Strings therefore require
valid UTF-8 and paired escaped UTF-16 surrogates. Rejecting lone surrogate escapes
is intentionally stricter than the RFC's permissive string ABNF, matching the
existing `JsonString` contract. The reader rejects a leading UTF-8 BOM, comments,
trailing commas, unescaped control bytes, and non-JSON whitespace. Duplicate object
names are emitted unchanged; no uniqueness or normalization policy is imposed.

Malformed input throws `JsonParseException`, whose `Offset` is an absolute byte
offset, possibly equal to the input count at unexpected EOF. Invalid string contents
report the opening quote's offset; unterminated strings report EOF. Messages give
a short reason, rather than a stable machine-readable error code.

After **any** exception from `Read`, including allocation failure, call `Reset()`
before reading again. A failed reader rejects further reads. `Reset()` returns to
the beginning, clears token metadata, and retains stack capacity. Input is never
modified, including on errors. Exception allocation follows normal runtime rules.

The caller keeps input alive and unchanged for the reader and every saved raw
slice. Token offsets can be saved as ordinary numbers but do not keep the input
alive. The reader owns only its stack and follows ordinary owning-value rules:
transfer it with `move`; it does not provide a copy constructor. No chunked-input,
incremental-document, or resumable-error protocol is provided.

For explicit source selection, include `JsonReader.gf`, `JsonTokenKind.gf`,
`JsonParseException.gf`, `JsonString.gf`, `Utf8.gf`, and the ordinary `Vector`
dependencies. The complete standard library includes them automatically.

Grammar reference: [RFC 8259](https://www.rfc-editor.org/rfc/rfc8259.html).
The token/depth conventions follow the familiar shape of
[.NET Utf8JsonReader](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.utf8jsonreader).
