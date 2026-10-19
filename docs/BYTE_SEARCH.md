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

The implementation uses the Two-Way algorithm: preprocessing factors the
pattern into two parts and establishes its period, then matching reuses that
information to avoid repeatedly checking long equal prefixes. Worst-case time
is linear in `length + count`, with constant extra storage and no pattern-size
stack buffer. The reverse search applies the same algorithm to virtual reversed
views of the input and pattern; it does not copy or reverse their bytes.

This is deliberately a search primitive, not Unicode text matching: it performs
no decoding, case folding, or normalization.

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
