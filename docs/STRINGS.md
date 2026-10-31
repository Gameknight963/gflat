# Strings

`Standard::Text::String` owns a byte buffer with a trailing NUL. `Length` counts
payload bytes, including embedded NULs. Copies own independent storage, while
`move` transfers storage.

```csharp
using Standard::Text;
using Standard;

String text = s" hello ";
text.Trim();
text.Append('!');
String greeting = text.Substring(0, 5);
bool found = text.Contains("ell");
nuint offset = text.IndexOf('!');
```

## Construction and views

| Operation | Result |
| --- | --- |
| `new String()` | Empty String |
| `String::From(text)` | Copies a C string before its first NUL |
| `String::From(data, count)` | Copies counted bytes |
| `Data`, `ToCString()` | Readonly pointer into owned storage |
| `AsSpan()` | Mutable `Span<char>` over `Length` bytes |
| `AsReadOnly()`, `AsImmutable()` | `Span<readonly(char)>` over `Length` bytes |

Retain the String owner while using its pointers or views. Reallocation,
structural edits and destruction can invalidate them. Moving transfers storage
to the new owner. A view from a
temporary String lasts only through that expression. Empty spans have null data.

## Equality, ordering, and search

`Equals` and `CompareTo` accept a String pointer, C string, or pointer and byte
count. They compare unsigned bytes. `CompareTo` returns negative, zero, or
positive for less, equal, or greater.

```cpp
using Standard::Text;
using Standard;

int main()
{
    String version = s"item-20";
    if (version.CompareTo("item-3") >= 0) return 1;
    char[3] bytes = ['a', '\0', 'b'];
    String binary = String::From(bytes, 3);
    if (binary.CompareTo(bytes, 3) != 0) return 2;
    if (binary.CompareTo(bytes) <= 0) return 3;
    return 0;
}
```

`StartsWith`, `EndsWith`, `Contains`, `IndexOf`, and `LastIndexOf` accept C-string
or counted patterns. Character searches accept a single `char`. Counts and
offsets are bytes. Use [Utf8](UTF8.md) for scalar boundaries.

| Search case | Result |
| --- | --- |
| Missing match | `String::NotFound`, the maximum `nuint` |
| Empty pattern, `IndexOf` | Search start |
| Empty pattern, `LastIndexOf` | `Length` |

Patterns may borrow the String's own bytes. Counted patterns include NUL, while
C strings end there. For example, `s"a\0b".Equals("a")` is false because the
String still has three bytes. Substring search takes O(text length + pattern length).

## Editing

| Method | Effect |
| --- | --- |
| `Append(value)` | Adds a character, C string, counted bytes, or another String's bytes |
| `Insert(index, value)` | Inserts before a byte offset. `Length` appends. |
| `Remove(start, count)` | Removes bytes and shifts the suffix |
| `Replace(char, char)` | Changes matching bytes in place |
| `Replace(oldText, newText)` | Replaces nonoverlapping matches from left to right |
| `Trim`, `TrimStart`, `TrimEnd` | Remove ASCII whitespace in place |
| `PadLeft(width, byte)`, `PadRight(width, byte)` | Grow to a byte width, using space by default |
| `Substring(start, count)` | Returns an independent String |

Range errors throw. Append and Insert support input from the same String,
including initialized spare bytes. String-pattern Replace requires a nonempty
old pattern. An empty replacement removes matches. A match builds replacement
storage before committing, while no match retains the original storage.

Trimming removes space, tab, CR, LF, vertical tab, and form feed. Padding and
offsets measure bytes rather than displayed columns. For line-ending conversion,
use [`ReplaceLineEndings`](LINE_ENDINGS.md).

## Capacity and length

- `Reserve(capacity)` grows storage while preserving the counted contents
- `Resize(length, fill = '\0')` preserves the prefix and fills new bytes
- `Clear()` empties the String while retaining capacity
- `ShrinkToFit()` reduces capacity to Length, releasing storage when empty

Shrinking length and trimming retain spare capacity. Allocating edits prepare
new storage before replacing it, so size overflow or allocation failure preserves
the original String. Successful buffer replacement invalidates old borrows.

## Repeated counted bytes

`Repeat(times)` returns a String containing that many copies of the complete
counted contents. Zero repetitions or empty input returns empty. Result-size
overflow throws `Exception`, and allocation failure throws `BadAllocException`.

```csharp
using Standard;
using Standard::Text;

int main()
{
    String source = s"a\0b";
    String repeated = source.Repeat(3);
    if (repeated.Length != 9 || repeated.Data[9] != '\0') return 1;
    for (nuint i = 0; i < repeated.Length; i++)
        if (repeated.Data[i] != source.Data[i % source.Length]) return 2;
    repeated[0] = 'x';
    if (source.Data[0] != 'a') return 3;
    String empty = source.Repeat(0);
    if (!empty.IsEmpty || empty.Data[0] != '\0') return 4;
    return 0;
}
```

Sources: `core/Text/String.gf`, `string_span.gf` for views, and `Text/ByteSearch.gf`
for substring search.
