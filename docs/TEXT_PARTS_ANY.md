# Splitting on a byte set

`TextPartsAny` lazily splits borrowed counted text whenever it encounters any
delimiter byte. `text_parts_any_span.gf` adds `SplitAny` to `Span<char>` and
`Span<readonly(char)>`; optional `string_split_any.gf` adds it to a borrowed
`ref immutable(String)` receiver, rejecting temporary owning receivers.

```csharp
using std; using std::Memory;
int main()
{
    String text = s"one,two;three";
    nuint count = 0;
    foreach (Span<readonly(char)> part in text.SplitAny(",;"))
    {
        if (part.Length != 3 && part.Length != 5) return 1;
        count++;
    }
    return (int)count - 3;
}
```

Both constructors take source `Span<readonly(char)>`, either counted
`Span<readonly(char)> separators` or null-terminated `readonly(char)* separators`,
and optional `bool removeEmpty = false`. The same arguments follow each extension
receiver. Counted separators can contain NUL and all high byte values. A C string
stops at its first NUL. Duplicates are immaterial. An empty set means no delimiter,
including when the counted separator span is default; it is not a whitespace mode.

Construction snapshots the set into four `ulong` masks. No separator pointer is
retained, so separator storage can change or be destroyed immediately afterwards.
Successful construction and enumeration allocate nothing. Construction scans the separators once; enumeration
scans source bytes with constant-time membership tests. No Unicode decoding,
multi-character delimiter matching, quoting, or shell parsing is performed.

Leading, consecutive, and trailing delimiters preserve empty parts by default.
Empty input yields one empty part. `removeEmpty` skips all empty parts. With no
delimiters the whole source is one part, unless it is empty and empties are removed.
Each `Current` returns a readonly borrowed span without copying its bytes. It throws
before the first successful `MoveNext` and after the iterator finishes. Repeated
`Current` observes the same slice; repeated calls after the end stay false.

`GetEnumerator` creates independent cursors, and `AsEnumerable` supplies the normal
query wrapper. The source, all cursors, and returned parts borrow the original
storage: keep it alive and at the same address. Mutating a source byte through
another alias is visible through parts and can affect delimiters ahead of a cursor.
The source is not snapshotted or protected from unrelated mutable aliases.

For selective compilation include `Text/TextPartsAny.gf`, `text_parts_any_span.gf`,
`Memory/Span.gf`, `Collections/IEnumerable.gf`, `Collections/IEnumerator.gf`, and
`Queries/Enumerable.gf`. The optional String adapter needs the usual String
dependencies. Normal query operators need their ordinary source files.
`NoStdLib` builds also supply the normal exception contracts.
