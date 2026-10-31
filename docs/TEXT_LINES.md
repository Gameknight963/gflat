# Borrowed text lines

`Standard::Text::TextLines` yields readonly spans over lines in a byte buffer.
String and character spans also offer `.Lines()`.

```csharp
using Standard::Text;
using Standard;
using Standard::Memory;

Span<readonly(char)> text = new("first\r\nsecond\n", 14);
TextLines lines = new(text);
foreach (Span<readonly(char)> line in lines) {
    Console::WriteLine(line.Data!, line.Length);
}
```

| Input | Lines |
| --- | --- |
| Empty | None |
| `"\n"` | One empty line |
| `"a\n\n"` | `"a"`, then an empty line |
| Final unterminated text | One final line |

LF and CRLF end a line and are excluded from its span. Bare CR and embedded NUL
remain data. A final terminator creates no additional line.

## Keeping line views

Retain the input owner while using its lines, iterators, or queries. String
destruction and buffer replacement can invalidate them. A view from a temporary
String lasts only through that expression. Changes through other aliases are
visible through existing line views.

Each `GetEnumerator()` starts at the beginning. `Current` throws before the first
successful advance and after completion. `AsEnumerable()` enables query operators
over the same borrowed storage.

Sources in `core/Text`: `TextLines.gf`, `text_lines_span.gf`, and `string_lines.gf`.
