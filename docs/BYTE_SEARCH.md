# Counted byte searches

`Standard::Text::ByteSearch` searches byte sequences and byte sets.

| Parameter | Meaning |
| --- | --- |
| `data` | Buffer to search |
| `length` | Number of bytes in `data` |
| `pattern` | Substring or byte set to find |
| `count` | Number of bytes in `pattern` |

```csharp
using Standard::Text;

nuint first = ByteSearch::IndexOf(data, length, pattern, count);
nuint last = ByteSearch::LastIndexOf(data, length, pattern, count);
```

Results are offsets into `data`. Matching includes all byte values, including NUL.
Input and pattern may overlap.

| Operation | Result |
| --- | --- |
| `IndexOf` | First matching substring |
| `LastIndexOf` | Last matching substring |
| Either, when absent | `length` |
| `IndexOf`, empty pattern | 0 |
| `LastIndexOf`, empty pattern | `length` |

Overlapping matches count: the last occurrence of `[1,1]` in `[1,1,1]` starts
at one. The Two-Way algorithm gives O(`length + count`) worst-case time and
constant working storage.

## Searching for any member of a byte set

`IndexOfAny` and `LastIndexOfAny` find the first or last byte belonging to a
counted set. Set order and duplicates have no effect. An empty input or set
returns `length`.

```csharp
using Standard;
using Standard::Text;

int main()
{
    byte[6] data = [7, 0, 255, 3, 255, 8];
    byte[3] members = [255, 0, 255];
    if (ByteSearch::IndexOfAny(data, 6, members, 3) != 1) return 1;
    if (ByteSearch::LastIndexOfAny(data, 6, members, 3) != 4) return 2;
    if (ByteSearch::IndexOfAny(data, 6, members, 0) != 6) return 3;
    return 0;
}
```

The set searches use a 256-bit membership mask and take O(`length + count`) time.

## String searches

`String.IndexOfAny(values, count, start = 0)` and
`String.LastIndexOfAny(values, count)` search the String's bytes and return
`String::NotFound` when absent. An empty set or String has no match.

`IndexOfAny` returns an absolute byte offset. `start > Length` throws, while
`start == Length` returns `NotFound`. A set may borrow the String's own bytes.
The C-string overloads stop the set at its first NUL. Use a counted overload
to search for NUL itself.

## Attribution

The Two-Way loops are adapted from musl's
[`src/string/memmem.c`](https://git.musl-libc.org/cgit/musl/tree/src/string/memmem.c).
The upstream code uses the following [MIT license](https://git.musl-libc.org/cgit/musl/tree/COPYRIGHT):

Copyright © 2005-2020 Rich Felker, et al.

Permission is hereby granted, free of charge, to any person obtaining
a copy of this software and associated documentation files (the
"Software"), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to
permit persons to whom the Software is furnished to do so, subject to
the following conditions:

The above copyright notice and this permission notice shall be
included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## Searching outside a byte set

`IndexOfAnyExcept` and `LastIndexOfAnyExcept` find the first or last byte
absent from the set. No match returns `length`.

| Input | `IndexOfAnyExcept` | `LastIndexOfAnyExcept` |
| --- | --- | --- |
| Empty data | 0 | 0 |
| Nonempty data, empty set | 0 | `length - 1` |

```csharp
using Standard::Text;

int main()
{
    byte[6] data = [7, 0, 255, 3, 255, 8];
    byte[3] members = [255, 0, 255];
    if (ByteSearch::IndexOfAnyExcept(data, 6, members, 3) != 0) return 1;
    if (ByteSearch::LastIndexOfAnyExcept(data, 6, members, 3) != 5) return 2;
    if (ByteSearch::IndexOfAnyExcept(data, 6, members, 0) != 0) return 3;
    if (ByteSearch::LastIndexOfAnyExcept(data, 6, members, 0) != 5) return 4;
    return 0;
}
```

Source: `core/Text/ByteSearch.gf`
