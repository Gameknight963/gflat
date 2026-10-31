# Quoting and unquoting a UTF-8 JSON string

## Owning String conveniences

`Standard::Json::JsonStringText::Quote` creates a quoted JSON literal.
`Unquote` creates its decoded UTF-8 String. Each accepts a pointer and byte count
or a `Span<readonly(char)>`, such as `reader.RawToken`.

```csharp
using Standard::Text;
using Standard;
using Standard::Json;

int main()
{
    String source = s"line1\nline2\0";
    String quoted = JsonStringText::Quote(source.AsReadOnly());
    String decoded = JsonStringText::Unquote(quoted.AsReadOnly());
    if (!decoded.Equals(&source)) return 1;

    source.Clear();
    quoted.Clear();
    if (decoded.Length != 12 || decoded[0] != 'l' || decoded[11] != '\0') return 2;
    return 0;
}
```

The complete input is validated before result allocation. Invalid text throws
Exception. The result owns its bytes, including any decoded NUL.

## Caller-owned buffers

`JsonString` offers buffer operations:

```csharp
using Standard::Json;

nuint needed = 0;
if (JsonString::TryGetQuotedLength(input, length, ref needed))
{
    nuint written = 0;
    bool success = JsonString::TryQuote(input, length, output, capacity, ref written);
}
```

| Operation | Result |
| --- | --- |
| `TryGetQuotedLength(input, count, ref length)` | Required bytes, including both quotes |
| `TryQuote(input, count, output, capacity, ref written)` | Quoted UTF-8 literal |
| `TryGetUnquotedLength(input, count, ref length)` | Decoded UTF-8 byte count |
| `TryUnquote(input, count, output, capacity, ref written)` | Decoded UTF-8 contents |

Outputs are counted and omit a NUL terminator. Empty text quotes as `""` and
requires two bytes. Invalid UTF-8, length overflow, or short capacity returns
false, zeros the output count, and preserves the output buffer.

Quoting escapes `"`, `\`, and control bytes. Backspace, tab, LF, form feed, and
CR use their short escapes. Other controls use uppercase `\u00XX`. Valid
non-ASCII UTF-8 is preserved. The policy follows
[RFC 8259 section 7](https://www.rfc-editor.org/rfc/rfc8259#section-7).

## Unquoting one complete literal

Input must consist of one complete quoted literal. Whitespace outside the quotes
or trailing data fails. Standard escapes and paired `\uXXXX` surrogates decode
to UTF-8. Unpaired surrogates, unknown escapes, and unescaped control bytes fail.
`\u0000` becomes a counted NUL byte.

The empty literal succeeds with length zero. The entire input is validated before
writing, even when malformed text follows a valid prefix.

## Borrowing and buffer requirements

Quoting needs separate input and output buffers. Unquoting also permits output
at the input's exact starting address. Other partial overlap is unsupported.
Keep scalar count outputs separate from both byte buffers.

## Comparing decoded text

`ValueTextEquals(input, count, text, length)` compares a quoted literal to decoded
UTF-8 text. For example, `"na\u006de"` matches `name`, and escaped surrogate pairs
match their UTF-8 scalar. Input regions may overlap.

A mismatch or malformed text returns false. Comparison can stop early. Use
`TryGetUnquotedLength` when full-literal validation is needed.

## Writing through ITextWriter

```csharp
using Standard::Json;

extension ITextWriter {
    public void WriteJsonString(readonly(char)* input, nuint length);
}
```

```csharp
using Standard::Json;

char[128] storage;
Standard::Text::BufferWriter buffer = new(storage, 128);
ITextWriter* output = &buffer;
output.WriteJsonString("hello", 5);
```

`WriteJsonString` validates input before writing, then sends quotes, unescaped
runs, and escapes to the destination. A destination failure can leave partial
output. Keep input separate from storage the destination may modify.

Sources: `core/Json/JsonString.gf`, `JsonStringText.gf`, and `json_string_writer.gf`
