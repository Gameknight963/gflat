# Borrowed JSON Pointer lookup

`Standard::Json::JsonPointer::TryResolve(document, pointer, ref value)` locates a
JSON value using a [plain JSON Pointer](https://www.rfc-editor.org/rfc/rfc6901.html#section-4).
All three arguments use `Span<readonly(char)>`.

```csharp
using Standard;
using Standard::Json;
using Standard::Memory;

int main()
{
    Span<readonly(char)> document = new("{\"users\":[{\"name\":\"Ada\"}]}", 26);
    Span<readonly(char)> pointer = new("/users/0/name", 13);
    Span<readonly(char)> value = new();
    if (!JsonPointer::TryResolve(document, pointer, ref value)) return 1;
    if (value.Length != 5 || value[0] != '"' || value[4] != '"') return 2;
    if (value[1] != 'A' || value[2] != 'd' || value[3] != 'a') return 3;
    return 0;
}
```

The returned span borrows the selected value's exact bytes, including quotes or
container whitespace. Keep its document owner alive. An empty pointer selects
the root value.

## Pointer syntax

- Start a nonempty pointer with `/`, with one segment per level
- Escape `~` as `~0` and `/` as `~1`
- Use empty segments for empty object names
- Use zero-based decimal array indices, with no leading zeroes except `0`

Pointer input is UTF-8 text. For a URI fragment or JSON string representation,
decode that wrapper first. Escapes are decoded once, so `~01` addresses `~1`.

Object names are compared after decoding JSON string escapes. If the requested
name matches duplicate members, lookup returns false. Other duplicate names
do not prevent lookup.

## Results and errors

Success replaces `value`. Missing members, invalid pointer syntax, invalid array
indices, or descent through a scalar return false and preserve it.

The entire JSON document is validated first. Invalid JSON throws JsonParseException
even when the pointer would fail. Validation and segment decoding can allocate,
and allocation failure can throw BadAllocException. Exceptions also preserve
the previous output.

Descent rescans parent values, taking O(pointer length + document length ×
(pointer depth + 1)) worst-case work.

Source: `core/Json/JsonPointer.gf`
