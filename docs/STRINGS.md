# Strings

`std::String` owns a null-terminated byte buffer. `Length` excludes the terminator;
embedded zero bytes count toward its length. A copy owns independent storage,
and `move` transfers storage. `Data` and `ToCString()` borrow storage until the
string is mutated, moved, or destroyed. `String::From(text)` reads a C string;
`String::From(text, count)` preserves every counted byte.

```csharp
using std;
String text = s" hello ";
text.Trim();
text.Append('!');
String greeting = text.Substring(0, 5);
bool found = text.Contains("ell");
nuint offset = text.IndexOf('!');
```

Search, indexing, slicing, and removal use byte offsets. They do not split on
Unicode character boundaries automatically. UTF-8 validation and scalar decoding
are available separately through `std::Utf8`.

The optional `StringSpan.gf` source adds `text.AsSpan()`, `text.AsReadOnly()`, and
`text.AsImmutable()`. All borrow exactly `Length` bytes, including embedded zeros, without copying the
owner or allocating. `AsSpan` requires a mutable `ref String` receiver and returns
`Span<char>`. `AsReadOnly` accepts `ref readonly(String)`; `AsImmutable` accepts
`ref immutable(String)`. Both return `Span<readonly(char)>`: shallow and deep
protection are equivalent for individual characters. Temporary owners are rejected. Empty views have null data,
even when the String retains an allocation; neither spare capacity nor the shared
empty sentinel or trailing terminator is exposed through these views.

Keep the owner alive and at a stable address. Growth, moving/destroying the owner,
and structural changes invalidate views. Assigning individual bytes through a
mutable span changes the String without changing its length; it does not validate
UTF-8. Slice bounds remain byte bounds. End a view's use before calling `Remove`,
`Append`, or another structural operation. These are raw borrows, without lifetime
tracking. Explicit source selections also need the ordinary `Span` dependencies;
`String.gf` alone does not acquire a dependency on Span.

`IndexOf` and `LastIndexOf` return `String::NotFound` (the largest `nuint`) when
no match exists. An empty needle matches at the search start for `IndexOf`,
and at `Length` for `LastIndexOf`.
`StartsWith`, `EndsWith`, `Contains`, `IndexOf`, and `LastIndexOf` accept C strings
or counted byte sequences. `LastIndexOf` also accepts a single `char`, searches
the entire string backwards, and includes overlapping occurrences. Counted
patterns preserve embedded zero bytes; C-string patterns stop at their first
zero. Search patterns may borrow the string's own storage. Forward character
search also accepts a start offset. Invalid start and
substring/removal ranges throw instead of wrapping pointer-sized arithmetic.

Substring searches reuse `std::ByteSearch`'s Two-Way search with constant extra
storage, no allocation, and O(string length + pattern length) worst-case work.
Character search is linear. Explicit source selections containing `String.gf`
must also include `ByteSearch.gf`; projects referencing the complete standard
library already include it.

`Substring` allocates an independent string. `Remove`, character `Replace`, and
ASCII-whitespace `Trim`, `TrimStart`, and `TrimEnd` modify the existing buffer
without allocation. Trimming preserves its capacity and storage address, removes
only space, tab, CR, LF, vertical tab, and form feed, and maintains the trailing
zero byte. Embedded zero bytes and non-ASCII bytes are not whitespace. `Append`
accepts a character, C string, counted bytes, or borrowed String pointer; appending
from the string's own buffer is supported with or without growth.

`PadLeft(width, padding = ' ')` and `PadRight(width, padding = ' ')` extend the
same string to a total byte width. They never truncate and do nothing when
`width <= Length`. Padding is one byte, not a Unicode scalar or display column;
zero and non-ASCII padding bytes are allowed and count toward `Length`. Existing
capacity is reused without allocation. Otherwise, one reservation happens before
any mutation, so allocation failure or an impossible width preserves the original
storage, capacity, and contents. The trailing zero byte is maintained separately
from any counted zero padding.

`Insert(index, text)` accepts a C string, counted byte sequence, character, or
borrowed String pointer. Inserting at `Length` appends. Input borrowed from the
same string is snapshotted before mutation, including when existing capacity is
sufficient or trimming left readable input beyond `Length` within that capacity.
Other input needs no temporary allocation. Invalid indices, length
overflow, and allocation failure leave the original contents unchanged. As with
all counted APIs, the input must cover the requested readable bytes.

`Replace(oldText, newText)` and the counted overload
`Replace(oldText, oldCount, newText, newCount)` replace nonoverlapping matches
from left to right. The old pattern must be nonempty; an empty replacement
removes matches. Inputs can borrow the original string's storage. A matching
replacement builds a separate buffer and commits it only after success, so
allocation failure preserves the original. If no match exists, no allocation
or mutation occurs. Matching replacements invalidate previous storage views.

Equality, comparison, and hashing use complete byte contents, including embedded
zero bytes. Ordering is unsigned byte order, independent of locale. String does
not require valid UTF-8; binary callers may store arbitrary bytes.

Empty construction and the `Length`, `Capacity`, `IsEmpty`, and `Data` getters
are explicitly `nothrow`. So are `ToCString()`, `Equals`, `CompareTo`,
`GetHashCode`, `StartsWith`, `EndsWith`, `LastIndexOf`, `Trim()`, `TrimStart()`,
`TrimEnd()`, `Clear()`, and character-to-character
`Replace`. These operations allocate
nothing under their input memory preconditions. Allocating operations, indexed
access, ranges, and forward searches retain their existing exception contracts.
