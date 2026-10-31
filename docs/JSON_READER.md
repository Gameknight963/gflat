# JSON reader

`Standard::Json::JsonReader` reads tokens from a complete UTF-8 buffer:

```cpp
using Standard;
using Standard::Json;
using Standard::Memory;

readonly(char)* document = "{\"ready\":true}";
JsonReader reader = new(document, 14);
while (reader.Read()) {
    JsonTokenKind kind = reader.Kind;
    Span<readonly(char)> raw = reader.RawToken;
    nuint count = raw.Length;
    // Process the counted raw lexeme here.
}
```

`Read()` returns true for the next token and false after validating one root
value plus trailing JSON whitespace. Read until false when validating a document.
Empty input, incomplete values, and trailing non-whitespace throw.

| Member | Meaning |
| --- | --- |
| `Kind` | Start/end object or array, PropertyName, String, Number, True, False, Null, or None |
| `TokenOffset`, `TokenLength` | Current lexeme's byte range in the input |
| `Depth` | Number of enclosing containers. Root opening and closing tokens have depth zero. |
| `RawToken` | Readonly span containing exact quotes, escapes, number spelling, or one container delimiter |
| `ValueTextEquals(text, count)` | Compares decoded contents of a String or PropertyName token |

Numbers retain their source spelling and can exceed machine numeric ranges.
Commas and colons are consumed as grammar between tokens.

Keep input alive and unchanged for the reader and saved RawToken spans. Saved
spans remain usable after the reader advances or is destroyed, provided their
input survives. Before reading, after completion, or after failure, RawToken is
empty and Kind is None.

## Skipping unknown values

`Skip()` on a property name first reads its value. For an object or array, it
advances through the matching closing token and leaves it current. For a scalar,
end token, or None, position stays unchanged. The next Read reaches the next
sibling or parent delimiter.

```cpp
using Standard::Json;

while (reader.Read()) {
    if (reader.Kind == JsonTokenKind::PropertyName) {
        // Decode/compare the name if needed. Ignore this unknown property's value.
        reader.Skip();
    }
}
```

Skipped content is validated. Continue reading afterward to validate the rest of
the document. To decode a string token into a buffer, pass RawToken to
[JsonString](JSON_STRING.md). For lookup by path, use [JsonPointer](JSON_POINTER.md).

## Input and errors

Input follows [RFC 8259](https://www.rfc-editor.org/rfc/rfc8259.html), with strict
UTF-8 and paired escaped surrogates. Comments, trailing commas, and a leading BOM
are rejected. Duplicate property names are emitted as encountered.

Malformed input throws JsonParseException. Its Offset is a byte position, possibly
the end of input. Invalid string contents identify their opening quote, while
unterminated strings identify EOF. Nesting-stack growth can throw BadAllocException.

Any Read or Skip exception requires `Reset()` before further reading. Reset
restarts the same input and retains stack capacity. The reader is move-only.

Source: `core/Json/JsonReader.gf`
