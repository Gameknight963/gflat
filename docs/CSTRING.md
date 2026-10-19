# Borrowed C strings

`std/core/CString.gf` provides standalone `std::CString` helpers for raw
null-terminated strings. Its source has no String, Span, allocation, libc, or
exception-runtime calls. Every operation is `nothrow`.

Native optimization can introduce a libc call even when the source contains
none: LLVM recognizes the unbounded length scan as `strlen`. A native build
without libc therefore also needs `CompilationOptions.NoBuiltins = true` to
disable introducing such library calls. The
[freestanding executable test](FREESTANDING_CORE.md) verifies all six helpers at
O0 and O2 with that policy. Explicit memory intrinsics and other platform ABI helpers
have separate lowering rules; source independence alone is not a proof that a
final binary has no runtime imports.

```csharp
nuint length = std::CString::Length("hello");
bool same = std::CString::Equals("hello", "hello");
int order = std::CString::Compare("apple", "pear");
bool prefix = std::CString::StartsWith("hello", "hell");
bool suffix = std::CString::EndsWith("hello", "llo");
```

All parameters are borrowed `readonly(char)*` pointers. These helpers neither
own nor retain them, and do not free or modify their bytes. The caller keeps
each region alive for the duration of the call.

`Length` counts bytes before the first zero byte. `Compare` returns -1, 0, or 1
under unsigned-byte lexicographic ordering, so bytes 128–255 sort after 127
regardless of the signed representation of `char`. `Equals` uses that same
ordering. No bytes after the first terminator contribute to an operation.
Empty prefixes and suffixes match every string, including the empty string.

These are byte operations, with no Unicode decoding, normalization, case
folding, locale, or grapheme handling. Unbounded operations require every input
to contain a readable terminator. `nothrow` does not make invalid pointers or
unterminated regions safe.

## Bounded length

```csharp
nuint length = 0;
bool terminated = std::CString::TryGetLength(buffer, capacity, ref length);
```

`capacity` is the number of readable bytes beginning at `buffer`, including
space for a terminator. The scan never reads outside this region. It returns
true and the number of preceding bytes if it finds a terminator, otherwise false
and zero. Capacity zero performs no input read and returns false. Pointer
parameters remain nonnullable under the normal type rules.

The scalar output is written only after the scan completes. It may overlap the
input when it is valid, naturally aligned `nuint` storage. That final store can
overwrite bytes in the input region, including on failure when zero is stored;
the method does not promise to preserve bytes overlapping its explicit output.
