# Borrowed text splitting

`std/core/Text/TextParts.gf` provides `std::Text::TextParts`, splitting counted bytes on a
single-byte delimiter without copying or allocating. It takes a
`Span<readonly(char)>` and implements
`IEnumerable<Span<readonly(char)>, TextParts::Enumerator>`.

`GetEnumerator()` creates an independent iterator over the same source. Each
`Current` is a borrowed span, not an owning String. `Current` throws before the
first successful `MoveNext` and after iteration ends. Empty parts have length
zero and no readable element; do not dereference their Data pointer.

`GetEnumerator()` and `AsEnumerable()` accept mutable, shallow readonly, and
immutable sequences. `Current` can likewise be read through a readonly or
immutable iterator observer; advancing still requires a mutable iterator.
These qualifiers protect the view metadata and returned character access;
they do not freeze backing storage against writes through another alias.

By default leading, consecutive, and trailing delimiters preserve empty parts.
An empty input yields one empty part. Set `removeEmpty` to true to skip empty
parts; empty input then yields none. Embedded zero bytes remain ordinary bytes,
and zero itself can be used as the delimiter. This is byte splitting, not a
Unicode code-point or multi-character delimiter operation.

`text_parts_span.gf` adds `Split(delimiter, removeEmpty = false)` extensions for
`Span<char>` and `Span<readonly(char)>`. These copy only the borrowed view's
metadata. `string_split.gf` separately adds the same convenience to an owning
String through a `ref immutable(String)` receiver, avoiding a deep copy and
rejecting temporary String receivers. Neither extension file changes String or
Span's own source dependencies.

```csharp
using std::Text;
using std; using std::Memory;
String text = s"first,,last,";
TextParts parts = text.Split(',');
foreach (Span<readonly(char)> part in parts)
{
    // part is a view into text, including two empty parts in this example.
}
```

The source storage must remain alive and at the same address for every use of
the sequence, its iterators, and its returned spans. In particular, destroying,
moving, or reallocating an owning String can invalidate them. A reference
receiver prevents one easy temporary-lifetime mistake; it is not general
lifetime analysis. Mutations to the underlying bytes are visible through
existing parts and can change subsequent splitting; TextParts is not a snapshot.

`AsEnumerable()` exposes the existing concrete query wrapper without changing
these borrowing rules. Direct `foreach` is sufficient for ordinary iteration.

## Trimming borrowed text

`std/core/TextTrimSpan.gf` optionally adds `Trim()`, `TrimStart()`, and
`TrimEnd()` to `Span<char>` and `Span<readonly(char)>`. Include `Memory/Span.gf` and
`Text/ascii.gf` with this extension source. Unlike the owning String's mutating
methods, these return a sliced view and leave every source byte unchanged.

Trimming uses exactly `Ascii::IsWhiteSpace`: space, tab, LF, vertical tab,
form feed, and CR. Embedded zero bytes and all non-ASCII bytes are retained;
this is not Unicode whitespace trimming or UTF-8 decoding.

```csharp
using std::Memory;
foreach (Span<readonly(char)> part in text.Split('=')) {
    Span<readonly(char)> token = part.Trim();
    // Use token while text's underlying storage remains alive.
}
```

The returned view preserves the source's element mutability: trimming a
`Span<char>` returns a writable alias, and trimming a `Span<readonly(char)>`
does not permit writes to its elements. The operation does not change the
source view's metadata or allocate storage. Empty inputs and entirely trimmed
results use Span's canonical empty view, with zero length and a null Data
pointer. Normal Span slice contracts apply; these extensions do not add a
`nothrow` guarantee to the checked slicing API.

These views obey the same lifetime rules as split parts and line views. They
do not take ownership or extend the underlying allocation's lifetime.

## Preserve the remainder after one separator

For a key/value setting such as `mode=fast=checked`, use the optional
[`TrySplitOnce`](TEXT_SPLIT_ONCE.md) operation to obtain `mode` and `fast=checked`
without splitting the remaining equals sign. It returns two borrowed readonly
character spans and clears both outputs when no separator exists.
