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

`IndexOf` returns `String::NotFound` (the largest `nuint`) when no match exists.
An empty needle matches at the search start, including the end of the string.
`StartsWith`, `EndsWith`, `Contains`, and `IndexOf` accept C strings or counted
byte sequences. Character search also accepts a start offset. Invalid start and
substring/removal ranges throw instead of wrapping pointer-sized arithmetic.

`Substring` allocates an independent string. `Remove`, character `Replace`, and
ASCII-whitespace `Trim` modify the existing buffer without allocation. `Append`
accepts a character, C string, counted bytes, or borrowed String pointer; appending
from the string's own buffer is supported with or without growth.

`Insert(index, text)` accepts a C string, counted byte sequence, character, or
borrowed String pointer. Inserting at `Length` appends. Input borrowed from the
same string is snapshotted before mutation, including when existing capacity is
sufficient. Other input needs no temporary allocation. Invalid indices, length
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
