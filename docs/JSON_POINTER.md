# Borrowed JSON Pointer lookup

`JsonPointer::TryResolve(document, pointer, ref value)` selects a raw JSON value
using the plain pointer form of [RFC 6901 section 4](https://www.rfc-editor.org/rfc/rfc6901.html#section-4).
Both inputs and the output have type `Span<readonly(char)>` and contain counted
UTF-8 bytes. Success replaces the output span; false leaves it unchanged.

```csharp
using std; using std::Memory;
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

The result borrows the exact value bytes from `document`. Strings retain their
quotes and escape spelling; numbers retain their spelling; containers retain
internal whitespace. Whitespace surrounding the selected value is excluded.
An empty pointer selects the complete root value, including a scalar or empty
container. An empty document is invalid JSON.

The method first validates the **entire JSON document**, including its suffix,
before checking the pointer. Malformed JSON throws `JsonParseException` with an
absolute byte offset within the supplied document span, even when the pointer
is invalid or would miss a member. Pointer failure does not hide JSON errors.
Allocation failures can throw `BadAllocException`; the method is not `nothrow`.
These errors also leave the previous output unchanged.

Pointers must be valid UTF-8. A nonempty pointer begins with `/`; each slash
starts another segment. Empty segments address empty object names, including
the trailing segment of `/a/`. Counted pointers may contain embedded NUL bytes.
Only `~0` and `~1` escapes are accepted, decoded once to `~` and `/`; `~01`
therefore names `~1`, not `/`. The complete pointer's UTF-8 and escape syntax
is checked even if an earlier segment would not resolve.

Object names are compared after decoding JSON string escapes, with exact UTF-8
content and no Unicode normalization or case folding. A requested name that
matches multiple members returns false, including escaped-equivalent duplicates.
Duplicates with other names do not by themselves prevent lookup.
Array segments are zero-based ASCII decimal indices without leading zeros
except `0`. Empty, negative, signed, overflowing, out-of-range, and `-` indices
return false. Descent through a scalar, a missing member, or invalid pointer
syntax also returns false with the previous output unchanged.

This API accepts plain pointer text, not a JSON string literal or URI fragment;
it does not decode JSON quoting or percent escapes in the pointer argument.
Keep document and pointer bytes alive and unchanged throughout the call. Saved
result spans continue borrowing the document; they do not keep its owner alive.
The method neither mutates input bytes nor retains pointers outside the result.

Parsing may allocate reader nesting stacks and one reusable `String` for decoded
segments. There is no allocation-free guarantee, DOM, or owning result copy.
Validation adds one full document pass and scans the complete pointer. Descent
rescans parent values, with worst-case total work
O(pointer length + document length × (pointer depth + 1)).

Selective source users add `core/Json/JsonPointer.gf` with `JsonReader`, `JsonString`,
`Utf8`, `String`, and `Span` and their normal dependencies, including JSON token
and parse-exception definitions. Ordinary projects including std get it directly.
