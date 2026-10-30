# JSON reader

For path-based lookup returning a borrowed raw value, see
[JSON Pointer](JSON_POINTER.md).

`Standard::Json::JsonReader` reads one complete JSON document from a borrowed counted UTF-8
buffer. It emits tokens without allocating strings, converting numbers, or building
a DOM. Its `Vector<byte>` nesting stack grows as needed; there is no recursive
parser call stack or fixed nesting limit. Allocation can throw `BadAllocException`.

```cpp
using Standard; using Standard::Json; using Standard::Memory;

readonly(char)* document = "{\"ready\":true}";
JsonReader reader = new(document, 14);
while (reader.Read()) {
    JsonTokenKind kind = reader.Kind;
    Span<readonly(char)> raw = reader.RawToken;
    nuint count = raw.Length;
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

`RawToken` returns a borrowed `Span<readonly(char)>` over the exact current
lexeme, with length `TokenLength`: quotes and escapes remain present, number
spelling is unchanged, and container tokens contain their single delimiter.
It is available through mutable, readonly, and immutable reader views. The
getter copies no input bytes and allocates nothing. Before the first `Read`,
after completion or a failed read, and after `Reset`, it returns a canonical
empty span with null `Data` without accessing input storage.

A saved span borrows the original input rather than the reader. Advancing,
resetting, moving, or destroying the reader does not invalidate that span;
the caller must keep the original bytes alive and unchanged for every use.
The view neither owns those bytes nor prolongs their lifetime.

`ValueTextEquals(text, count)` compares a current `PropertyName` or `String`
with counted, unquoted UTF-8 text, without decoding into a buffer or allocating.
For example, `reader.ValueTextEquals("name", 4)` also matches `"na\u006de"`.
It has an immutable receiver and is `nothrow`. Comparison is exact decoded text;
case, normalization, and embedded nulls follow
[`JsonString::ValueTextEquals`](JSON_STRING.md#comparing-decoded-text).
For any other token kind or `None`, it returns false without examining `text`.
A mismatch or malformed expected UTF-8 returns false; the predicate may
short-circuit and is not a separate input-validation operation. Expected text
may overlap the borrowed input, but both must remain alive and unchanged.

## Skipping unknown values

`Skip()` ignores the current value's children without decoding strings or
building a DOM. On a `PropertyName`, it first reads the property's value. If
that value, or the original current token, opens an object or array, it reads
through the matching closing token. `Kind`, `TokenOffset`, `TokenLength`, and
`Depth` then describe that closing token. On a scalar, it leaves that scalar
current. The following `Read()` reaches the next sibling or containing delimiter.

On end tokens or `None` (before the first read or after completion), `Skip()`
does nothing. It does not finish the document or validate trailing data after
the skipped value; continue calling `Read()` until it returns false.

```cpp
using Standard::Json;
while (reader.Read()) {
    if (reader.Kind == JsonTokenKind::PropertyName) {
        // Decode/compare the name if needed. Ignore this unknown property's value.
        reader.Skip();
    }
}
```

Skipped content is fully validated through the ordinary `Read()` path, including
UTF-8, escapes, delimiters, and nested grammar. Malformed content still throws
`JsonParseException` with its original byte offset. Skipping is iterative and
uses the same nesting stack; growing it can throw `BadAllocException`. A failed
reader rejects `Skip()` as well as `Read()` until `Reset()`. Scalar/no-op skips
do not allocate or advance input.

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

After **any** exception from `Read` or `Skip`, including allocation failure, call `Reset()`
before reading again. A failed reader rejects further reads. `Reset()` returns to
the beginning, clears token metadata, and retains stack capacity. Input is never
modified, including on errors. Exception allocation follows normal runtime rules.

The caller keeps input alive and unchanged for the reader and every saved raw
slice. Token offsets can be saved as ordinary numbers but do not keep the input
alive. The reader owns only its stack and follows ordinary owning-value rules:
transfer it with `move`; it does not provide a copy constructor. No chunked-input,
incremental-document, or resumable-error protocol is provided.

For explicit source selection, include `Json/JsonReader.gf`, `Json/JsonTokenKind.gf`,
`Json/JsonParseException.gf`, `Json/JsonString.gf`, `Text/utf8.gf`, and the ordinary `Vector`
dependencies. The complete standard library includes them automatically.

Grammar reference: [RFC 8259](https://www.rfc-editor.org/rfc/rfc8259.html).
The token/depth conventions follow the familiar shape of
[.NET Utf8JsonReader](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.utf8jsonreader).
The skip positioning follows
[Utf8JsonReader.Skip](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.utf8jsonreader.skip).
