# Borrowed C strings

`Standard::Text::CString` works with NUL-terminated byte strings.

```csharp
using Standard::Text;

nuint length = CString::Length("hello");
bool same = CString::Equals("hello", "hello");
int order = CString::Compare("apple", "pear");
bool prefix = CString::StartsWith("hello", "hell");
bool suffix = CString::EndsWith("hello", "llo");
bool empty = CString::IsNullOrEmpty(null);
```

| Operation | Result |
| --- | --- |
| `Length(text)` | Byte count before the first NUL |
| `Compare(left, right)` | -1, 0, or 1 in unsigned-byte lexicographic order |
| `Equals(left, right)` | Exact equality |
| `StartsWith(text, prefix)`, `EndsWith(text, suffix)` | Exact prefix or suffix match |
| `IsNullOrEmpty(text)` | True for null or an initial NUL |

Empty prefixes and suffixes match every string.

## Character search

`IndexOf(text, value)` and `LastIndexOf(text, value)` return the first or last
matching byte offset. Absence returns `CString::NotFound`, the maximum `nuint`.
The terminator is excluded, so searching for NUL returns `NotFound`.

```csharp
using Standard::Text;

int main()
{
    readonly(char)* argument = "mode=fast=checked";
    nuint first = CString::IndexOf(argument, '=');
    nuint last = CString::LastIndexOf(argument, '=');
    if (first == CString::NotFound || last == CString::NotFound) return 1;
    if (first != 4 || last != 9) return 2;
    if (!CString::Equals(argument + first + 1, "fast=checked")) return 3;
    if (!CString::Equals(argument + last + 1, "checked")) return 4;
    return 0;
}
```

Offsets and derived pointers refer into the original string. Keep its owner
alive when retaining them.

## Bounded length

```csharp
using Standard::Text;

nuint length = 0;
bool terminated = CString::TryGetLength(buffer, capacity, ref length);
```

| Parameter | Meaning |
| --- | --- |
| `buffer` | Start of the string |
| `capacity` | Maximum readable bytes, including a possible terminator |
| `length` | Output count before the terminator |

`TryGetLength` returns false and zero when the region contains no NUL, including
when capacity is zero. An aligned output variable may overlap the input. Its
final store then overwrites those input bytes, even on failure.

## Bounded copy

```csharp
using Standard::Text;

char[16] output;
bool copied = CString::TryCopy("hello", output, 16);
```

`TryCopy(text, output, capacity)` copies the string and its terminator when they
fit. An empty string needs one byte. Failure preserves the destination.

The input scan reads through its first NUL or at most `capacity` bytes. Copies
allow overlap in either direction, including identical addresses.

## Bounded append

```csharp
using Standard::Text;

char[16] output;
CString::TryCopy("hello", output, 16);
bool appended = CString::TryAppend("!", output, 16); // hello!
```

`TryAppend(text, output, capacity)` appends to an existing C string. Capacity
covers the entire destination, including its prefix and final NUL. Failure
preserves the destination. An unterminated destination fails before reading
`text`.

The input scan is bounded by the remaining destination space. Appending the
destination to itself, or appending one of its suffixes, is supported.

Source: `core/Text/CString.gf`. For a freestanding build, use `NoBuiltins` to
prevent LLVM from replacing length loops with `strlen`. See
[freestanding startup and linking](FREESTANDING_CORE.md).
