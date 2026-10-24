# Counted byte searches

`std/core/ByteSearch.gf` is standalone: it needs neither String, Span, libc,
allocation, nor an exception runtime. All methods are `nothrow`.

```csharp
nuint first = std::ByteSearch::IndexOf(data, length, pattern, count);
nuint last = std::ByteSearch::LastIndexOf(data, length, pattern, count);
```

Both pointers borrow readable regions of the specified byte lengths. The
regions may overlap. Embedded zero bytes and every byte value are ordinary
data; no terminator is examined. A zero-length region requires no readable
bytes, but the pointer parameters remain nonnullable under the normal type
rules. Neither input changes, and no pointer is retained after the call.

Results are byte offsets relative to `data`. A missing pattern returns
`length`, matching the span algorithms' missing-index convention. An empty
pattern matches at zero for `IndexOf` and at `length` for `LastIndexOf`.
Overlapping matches count: the last occurrence of `[1,1]` in `[1,1,1]` is one.

The substring searches use the Two-Way algorithm: preprocessing factors the
pattern into two parts and establishes its period, then matching reuses that
information to avoid repeatedly checking long equal prefixes. Worst-case time
is linear in `length + count`, with constant extra storage and no pattern-size
stack buffer. The reverse search applies the same algorithm to virtual reversed
views of the input and pattern; it does not copy or reverse their bytes.

This is deliberately a search primitive, not Unicode text matching: it performs
no decoding, case folding, or normalization.

## Searching for any member of a byte set

`IndexOfAny(data, length, pattern, count)` returns the first data byte found
anywhere in the counted pattern set. `LastIndexOfAny` returns the last such byte.
They return `length` if there is no match. The pattern is a set of individual
bytes, not a substring: duplicates and order do not affect membership.

```csharp
using std;
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

If either length is zero, both methods return `length` before reading either
pointer. This differs from substring `IndexOf`'s empty-pattern result of zero.
All 256 byte values, including embedded NUL, participate normally. Both regions
are read-only and can overlap. The result is a byte offset, not a Unicode scalar
or character position; there is no text decoding or normalization.

A fixed four-word membership mask uses 32 bytes of stack storage. The pattern
is visited once, then the data is scanned in the selected direction until a
match. Worst-case work is O(`length + count`), independent of duplicate members.
No allocation, retained pointer, input mutation, or additional source dependency
is introduced. Both operations are `nothrow`. The membership-mask implementation
is original code; the existing substring search and its attribution are unchanged.

## Attribution

The factorization and matching loops are adapted from musl's
[`src/string/memmem.c`](https://git.musl-libc.org/cgit/musl/tree/src/string/memmem.c),
with readable reversed views, prefix-length rather than negative suffix indices,
and subtraction-based bounds checks. musl's optional bad-character tables and
special short-pattern routines are not included. The upstream code is covered
by the following [MIT license](https://git.musl-libc.org/cgit/musl/tree/COPYRIGHT):

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
