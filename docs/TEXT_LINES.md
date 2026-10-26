# Borrowed text lines

`std::TextLines` iterates counted text in memory without allocating a string per
line. It uses the same line endings as `FileLines` and `Console`: LF and CRLF
terminate lines; a bare CR remains text. Embedded null bytes are preserved.

```csharp
using std; using std::Memory;

Span<readonly(char)> text = new("first\r\nsecond\n", 14);
TextLines lines = new(text);
foreach (Span<readonly(char)> line in lines) {
    Console::WriteLine(line.Data!, line.Length);
}
```

The example produces two lines. Terminators are excluded from each view.
Empty input contains no lines. `"\n"` contains one empty line; `"a\n\n"`
contains `"a"` and an empty line. A trailing terminator does not introduce an
additional final line. An unterminated final line is included, preserving any
final bare CR.

The constructor takes `Span<readonly(char)>`. Build that span from a live
counted buffer, or from `String.Data` and `String.Length`. The input does not
need a null terminator, and no encoding conversion or validation is performed.

The optional `text_lines_span.gf` provides `.Lines()` for mutable and immutable
character spans. `string_lines.gf` provides `.Lines()` for a named `String`,
borrowing it through an immutable reference without copying its allocation.
Temporary owning strings cannot be receivers of this convenience: retain the
owner in a variable for as long as the line views are used. These optional
files keep the owning string and general span implementations independent of
line enumeration.

`GetEnumerator()` returns a fresh independent nested `Enumerator`.
`MoveNext()` remains false after completion. `Current` throws before the first
successful advance and after completion. Copying a view or iterator copies its
position and borrowed pointer, never its source bytes.

`GetEnumerator()` and `AsEnumerable()` accept mutable, shallow readonly, and
immutable sequences. `Current` can be read through a readonly or immutable
iterator observer; advancing still requires a mutable iterator. These views
prevent character writes through their returned spans, but do not freeze the
backing storage: writes through another live alias remain visible.

Keep the input storage alive and stable while using any view, iterator, or
returned line. Moving or resizing an owning source can invalidate them.
These views never free the source. Valid iteration allocates nothing.

`AsEnumerable()` enables the existing query operators when their source files
are included. It captures the borrowed view, so those same lifetime rules apply
to a query retained for later enumeration.
