# ASCII byte utilities

`core/Text/ascii.gf` provides `std::Text::Ascii` functions for the language's byte-sized
`char`. These functions do not interpret individual UTF-8 bytes as Unicode
characters. They work without std, libc, allocation, or exception support.

Classification functions are `IsAscii`, `IsDigit`, `IsLower`, `IsUpper`,
`IsLetter`, `IsLetterOrDigit`, `IsHexDigit`, `IsWhiteSpace`, `IsControl`, and
`IsPrintable`. Every function accepts a `char` and returns `bool`.

- ASCII comprises bytes 0 through 127.
- Letters are A–Z and a–z; decimal digits are 0–9.
- Hex digits additionally include A–F and a–f.
- Whitespace is space, tab, line feed, vertical tab, form feed, and carriage return.
- Control bytes are 0–31 and 127.
- Printable bytes are 32–126, including space.

All classification functions return false for bytes 128–255.
`ToLower(char)` and `ToUpper(char)` convert ASCII letters and preserve every
other byte, including bytes above 127. Results do not depend on locale or the
machine's signed `char` conventions.

`ToLower(char* data, nuint count)` and `ToUpper(char* data, nuint count)` convert
a caller-owned buffer in place using those same ASCII rules. Counts include
embedded NULs; these operations continue past them and leave every non-letter
byte unchanged. There is no null-terminated mutation overload.

```csharp
using std::Text;
char[4] bytes = ['A', '\0', 'z', (char)255];
std::Text::Ascii::ToLower(bytes, 4); // ['a', '\0', 'z', 255]
```

The caller supplies `count` writable bytes. Only that region is modified, and
aliases to it observe the changes immediately. A zero count performs no reads
or writes through the pointer. Pointers to readonly or immutable characters
cannot be passed implicitly. These overloads are `nothrow`, allocate nothing,
and add no source dependencies. They do not perform Unicode case conversion.

`TryHexValue(char input, byte* result)` recognizes one hexadecimal digit and
writes its numerical value from 0 through 15. Invalid input returns false and
writes zero. Use `std::Text::Utf8` to decode Unicode scalar values before applying
any future Unicode character operations.

## Counted case-insensitive comparison

These counted operations are also `nothrow` and allocation-free:

```csharp
using std::Text;
bool command = std::Text::Ascii::EqualsIgnoreCase(input, inputLength, "help", 4);
int order = std::Text::Ascii::CompareIgnoreCase(left, leftLength, right, rightLength);
```

Lengths count bytes, including embedded nulls. Buffers are borrowed and are never
modified. Each pointer must designate at least its supplied length of readable
storage; a zero length performs no reads through that pointer.

`CompareIgnoreCase` returns exactly -1, 0, or 1. It folds ASCII `a`-`z` to
uppercase, then compares unsigned bytes lexicographically. If the common prefix
is equal, the shorter input sorts first. Uppercase folding also determines
punctuation ordering: `z` sorts before `[`. For ASCII inputs, this agrees with
.NET's `StringComparison.OrdinalIgnoreCase` ordering.

Bytes 128-255 remain unchanged. This is not Unicode case folding, UTF-8 decoding,
or a locale-sensitive comparison. For example, differently cased accented letters
encoded in UTF-8 do not become equal. `EqualsIgnoreCase` returns false immediately
when lengths differ and otherwise applies the same ASCII-only rules.

## Null-terminated comparison

Two-argument overloads accept C strings:

```csharp
using std::Text;
bool command = std::Text::Ascii::EqualsIgnoreCase(input, "help");
int order = std::Text::Ascii::CompareIgnoreCase(left, right);
```

Both stop at the first null byte and use the same unsigned ASCII-only folding
and -1/0/1 ordering as the counted forms. Bytes after a terminator do not affect
the result. Both pointers must designate readable null-terminated strings.
These overloads are also `nothrow`, allocate nothing, and do not modify input.
Use the counted forms to compare embedded null bytes as part of the text.

## Case-insensitive hashing

`Ascii::HashIgnoreCase(data, count)` returns a `nuint` hash using the same ASCII
uppercase folding as counted `EqualsIgnoreCase`. It includes embedded NUL bytes
and leaves bytes 128-255 unchanged. Input is borrowed and never modified; provide
`count` readable bytes. Zero count reads no storage. The helper is `nothrow`,
allocates nothing, and adds no source dependencies.

Use this hash with the matching equality contract when preserving the original
spelling of case-insensitive keys. This example borrows only static string literals,
which remain alive for the entire map lifetime. Keys borrowing other storage must
keep it alive and unchanged while stored in the map.

```cpp
using std; using std::Text; using std::Collections;

struct HeaderName : IHashable, IEquatable<HeaderName>
{
    private readonly(char)* text;
    private nuint length;
    public HeaderName(readonly(char)* bytes, nuint count)
    {
        text = bytes;
        length = count;
    }
    public immutable nuint GetHashCode() nothrow
        => Ascii::HashIgnoreCase(text, length);
    public immutable bool Equals(immutable(HeaderName)* other) nothrow
        => Ascii::EqualsIgnoreCase(text, length, other.text, other.length);
}

int main()
{
    HashMap<HeaderName, int> fields = new();
    fields.Add(new HeaderName("Content-Type", 12), 7);
    HeaderName query = new("content-type", 12);
    int*? found = fields.Find(&query);
    if (found == null) return 1;
    if (*found! != 7) return 2;
    return 0;
}
```

The implementation currently uses 32-bit FNV-1a widened to `nuint`; its exact
numeric result is not a persistent-format contract. Like `Hashing::Bytes`, it is
not cryptographic and is unsuitable for keys supplied by hostile users because
collisions can be deliberately chosen. Hash equality alone does not establish
key equality: the map also calls the matching equality method.


## Counted case-insensitive substring search

`Ascii::IndexOfIgnoreCase(data, length, pattern, count)` and
`Ascii::LastIndexOfIgnoreCase(data, length, pattern, count)` return the first or
last matching start index, or `length` when absent. An empty pattern returns zero
for forward search and `length` for reverse search, the last insertion endpoint;
reverse empty-pattern success therefore shares the absent sentinel. An oversized
pattern returns `length`. Both preflights read neither pointer, including an
empty data/pattern pair.

Only ASCII letters fold, using the same comparison as `EqualsIgnoreCase`.
Embedded NULs are ordinary counted bytes; nonletters and bytes 128 through 255
must match exactly. This is not Unicode, locale-sensitive or C-string search.
Both inputs may overlap. For a nonempty pattern that fits, provide live readable
regions for the counted data and pattern and keep them stable during the call.

Candidate starts run left to right or right to left respectively, comparing
pattern bytes from left to right at each start. The implementation reuses the
existing counted equality routine: worst-case work is O(length*count), with
constant storage and no allocation, copies or mutation of the input buffers.
Both functions are `nothrow` and add no source dependency to `ascii.gf`.

```csharp
nuint first = Ascii::IndexOfIgnoreCase(text, textLength, "error", 5);
nuint last = Ascii::LastIndexOfIgnoreCase(text, textLength, "error", 5);
```
