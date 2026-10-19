# Integer parsing

`std` provides strict decimal parsers for `Byte`, `SByte`, `Short`, `UShort`,
`Int`, `UInt`, `Long`, `ULong`, `NInt`, and `NUInt`. Each name has four forms:

```cpp
bool TryParseLong(readonly(char)* text, long* result) nothrow;
bool TryParseLong(readonly(char)* text, nuint count, long* result) nothrow;
long ParseLong(readonly(char)* text);
long ParseLong(readonly(char)* text, nuint count);
```

The null-terminated form stops at the first null byte. The counted form consumes
exactly `count` bytes; an embedded null is invalid input. Pointers must designate
the requested readable input and writable result storage.

Every parser accepts decimal digits with an optional leading `+`. Signed parsers
also accept `-`. Unsigned parsers reject a minus sign, including `-0`. Leading
zeroes are accepted. Empty input, sign-only input, whitespace, separators, radix
prefixes, trailing characters, and values outside the result type's range fail.

`TryParse...` returns false and writes zero on every failure. `Parse...` throws
`Exception` on the same inputs. Overflow is checked before multiplication and
addition, including at the unsigned 64-bit boundary and the signed minimum.
Pointer-sized parsers use the target's pointer-sized integer limits.

The pointer-based `TryParse...` overloads explicitly guarantee `nothrow` and
allocate nothing under their memory preconditions. Their shared magnitude and
length helpers are also `nothrow`. The source files also contain the throwing
`Parse...` variants; including either complete file still requires the normal
exception definitions and runtime.

`core/Parsing.gf` supplies the existing `Int` functions. The other widths live
in `core/IntegerParsing.gf`; neither file depends on libc. Optional
`core/StringParsing.gf` and `core/StringIntegerParsing.gf` provide borrowed
readonly String extension methods:

```cpp
String text = s"18446744073709551615";
ulong number = text.ParseULong();
ulong attempted = 0ul;
bool valid = text.TryParseULong(&attempted);
```

These methods use `String.Length`, preserve embedded nulls for validation, and
do not copy or allocate string storage. The String `TryParse...` wrappers are
also explicitly `nothrow`; the `Parse...` wrappers retain their exceptions.
