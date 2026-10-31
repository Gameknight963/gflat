# Borrowed C strings

`Standard::Text::CString` provides allocation-free helpers for null-terminated
byte strings. Every operation is `nothrow`. For selective builds, include
`core/Text/CString.gf`.

Native optimization can introduce a libc call even when the source contains
none: LLVM recognizes the unbounded length scan as `strlen`. A native build
without libc therefore also needs `CompilationOptions.NoBuiltins = true` to
disable introducing such library calls. The
[freestanding executable test](FREESTANDING_CORE.md) verifies the scan/comparison helpers at
O0 and O2 with that policy. Explicit memory intrinsics and other platform ABI helpers
have separate lowering rules. Source independence alone is not a proof that a
final binary has no runtime imports.

```csharp
using Standard::Text;
nuint length = Standard::Text::CString::Length("hello");
bool same = Standard::Text::CString::Equals("hello", "hello");
int order = Standard::Text::CString::Compare("apple", "pear");
bool prefix = Standard::Text::CString::StartsWith("hello", "hell");
bool suffix = Standard::Text::CString::EndsWith("hello", "llo");
bool empty = Standard::Text::CString::IsNullOrEmpty(null);
```

Input parameters borrow `readonly(char)*` pointers for the duration of the call.
`IsNullOrEmpty` accepts `readonly(char)*?`. `TryCopy` and `TryAppend` write
only to their explicit destination buffers. The caller keeps
each region alive for the duration of the call.

`IsNullOrEmpty` returns true for null or a first byte of zero, and false otherwise.
It does not read a null pointer and inspects only the first byte of a nonnull
pointer. This can check optional results such as `Environment::GetVariable`
without scanning or allocating. All other pointer arguments retain their
documented nonnullable requirements.

`Length` counts bytes before the first zero byte. `Compare` returns -1, 0, or 1
under unsigned-byte lexicographic ordering, so bytes 128–255 sort after 127
regardless of the signed representation of `char`. `Equals` uses that same
ordering. No bytes after the first terminator contribute to an operation.
Empty prefixes and suffixes match every string, including the empty string.

## Character search

`IndexOf(text, value)` and `LastIndexOf(text, value)` return the first or last
matching byte offset in the logical string content. Missing values return
`CString::NotFound`, the maximum `nuint` value, matching `String::NotFound`.
The first NUL ends the content and is excluded from search: searching for `'\0'`
always returns `NotFound`, including in empty strings. This differs from C's
`strchr`/`strrchr`, which can return the terminator itself. Bytes after the first
NUL are never searched. Both helpers are `nothrow` and need only `CString.gf`.

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

The same calls accept borrowed command arguments such as `argv[1]` after checking
`argc`. The offsets and derived pointers borrow the original storage. No string
is copied or allocated. `IndexOf` stops at its first match or terminator.
`LastIndexOf` scans to the terminator while retaining the latest match. Inputs
remain nonnullable and must have a readable terminator.

These are byte operations, with no Unicode decoding, normalization, case
folding, locale, or grapheme handling. Unbounded operations require every input
to contain a readable terminator. `nothrow` does not make invalid pointers or
unterminated regions safe.

## Bounded length

```csharp
using Standard::Text;
nuint length = 0;
bool terminated = Standard::Text::CString::TryGetLength(buffer, capacity, ref length);
```

`capacity` is the number of readable bytes beginning at `buffer`, including
space for a terminator. The scan never reads outside this region. It returns
true and the number of preceding bytes if it finds a terminator, otherwise false
and zero. Capacity zero performs no input read and returns false. Pointer
parameters remain nonnullable under the normal type rules.

The scalar output is written only after the scan completes. It may overlap the
input when it is valid, naturally aligned `nuint` storage. That final store can
overwrite bytes in the input region, including on failure when zero is stored.
The method does not promise to preserve bytes overlapping its explicit output.

## Bounded copy

```csharp
using Standard::Text;
char[16] output;
bool copied = Standard::Text::CString::TryCopy("hello", output, 16);
```

`TryCopy(text, output, capacity)` copies through the first null, including that
terminator. Capacity includes the terminator, so an empty string needs one byte.
It scans at most `capacity` input bytes using `TryGetLength` before writing.
If no terminator fits, it returns false without changing any output byte.
Capacity zero returns false without reading or writing either buffer.

On success, exactly the string bytes and terminator are copied. Bytes beyond
the copied terminator remain unchanged. Input and output may overlap in either
direction or have the same address. Copying follows `memmove` order. Embedded
nulls end the input, so subsequent bytes are not part of the copy. Arbitrary
nonzero byte values are preserved without encoding validation.

Input must be readable through its first terminator, or for `capacity` bytes if
there is no terminator in that range. Output must designate writable storage for
the stated capacity. Keep both regions stable during validation and copying.
The method retains no pointer. The native-optimizer caveats above apply.

## Bounded append

```csharp
using Standard::Text;
char[16] output;
Standard::Text::CString::TryCopy("hello", output, 16);
bool appended = Standard::Text::CString::TryAppend("!", output, 16); // hello!
```

`TryAppend(text, output, capacity)` appends to an existing terminated string.
Capacity is the entire destination extent, including its existing prefix and
the final terminator. It first scans at most `capacity` destination bytes. If
none is zero, it returns false without reading `text`. Otherwise it scans at
most `capacity - currentLength` input bytes, enough to determine whether the
appended bytes and terminator fit. Neither scan reads beyond its stated bound.

Failure leaves every destination byte unchanged, including when input overlaps
output. Capacity zero reads neither pointer and returns false. Success preserves
the existing prefix and writes the appended bytes plus terminator, leaving the
remaining capacity unchanged. Appending empty text succeeds whenever the
destination already has a terminator, including when no further payload fits.

Input and output may overlap in either direction: appending the destination to
itself or appending one of its suffixes is supported. The input must be readable
through its first terminator, or for the remaining capacity if no terminator is
found. Output must be readable and writable for the full capacity. Keep both
regions stable during the call. Like `TryCopy`, this operation allocates nothing,
is `nothrow`, and uses no additional Standard source. The optimizer caveats above
remain applicable.
