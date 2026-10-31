# Borrowed text splitting

`text.Split(delimiter, removeEmpty = false)` yields `Span<readonly(char)>`
parts from a String or character span.

```csharp
using Standard::Text;
using Standard;
using Standard::Memory;

String text = s"first,,last,";
TextParts parts = text.Split(',');
foreach (Span<readonly(char)> part in parts)
{
    // part is a view into text, including two empty parts in this example.
}
```

Leading, consecutive, and trailing delimiters create empty parts. Empty input
yields one empty part. Set `removeEmpty` to true to skip them. A delimiter is one
byte and can be NUL.

`TextParts.GetEnumerator()` starts an independent cursor. `AsEnumerable()` enables
query operators. `Current` is valid after a successful advance and before the end.

## Keeping parts

Parts borrow the original storage. Retain the source owner while keeping a part,
iterator, or query. Destroying or reallocating a String can invalidate
all of them. A view from a temporary String lasts only through that expression.
Edits through another alias are visible in existing parts and can change later
delimiter matches.

## Trimming borrowed text

`Trim`, `TrimStart`, and `TrimEnd` return sliced spans:

```csharp
using Standard::Text;
using Standard::Memory;

foreach (Span<readonly(char)> part in text.Split('=')) {
    Span<readonly(char)> token = part.Trim();
    // Use token while text's underlying storage remains alive.
}
```

Whitespace is space, tab, LF, vertical tab, form feed, and CR. Other bytes,
including NUL, stay. Trimming a mutable span preserves writable element access.
An empty result has null `Data`.

## Preserve the remainder after one separator

For `mode=fast=checked`, [`TrySplitOnce`](TEXT_SPLIT_ONCE.md) returns `mode`
and `fast=checked`. Use it when the remainder is one value.

Sources in `core/Text`: `TextParts.gf`, `text_parts_span.gf`, `string_split.gf`,
and `text_trim_span.gf`.
