# Integer parsing

These APIs parse decimal text. Explicit unsigned hexadecimal alternatives are
documented in [Hexadecimal integer parsing](HEX_INTEGER_PARSING.md).

`std` provides strict decimal parsers for `Byte`, `SByte`, `Short`, `UShort`,
`Int`, `UInt`, `Long`, `ULong`, `ExtraLong`, `NInt`, and `NUInt`. Each name has four forms:

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
`ExtraLong` always uses the signed 128-bit range, from
`-170141183460469231731687303715884105728` through
`170141183460469231731687303715884105727`, independently of pointer size.
It checks a negative decimal accumulator before each step and needs no
128-bit division or remainder runtime helper.

Parsing finishes reading its input before committing a successful output, so
the output may overlap input storage when that storage is valid and suitably
aligned for the result type. On failure, writing the documented zero output
also changes any overlapping input bytes.

The pointer-based `TryParse...` overloads explicitly guarantee `nothrow` and
allocate nothing under their memory preconditions. Their shared magnitude and
length helpers are also `nothrow`. The source files also contain the throwing
`Parse...` variants; including either complete file still requires the normal
exception definitions and runtime.

`core/Parsing.gf` supplies the existing `Int` functions. The other widths live
in `core/IntegerParsing.gf`; neither file depends on libc. Optional
`core/StringParsing.gf` and `core/StringIntegerParsing.gf` provide borrowed
immutable String extension methods:

```cpp
using std::Text;
String text = s"18446744073709551615";
ulong number = text.ParseULong();
ulong attempted = 0ul;
bool valid = text.TryParseULong(&attempted);
```

These methods use `String.Length`, preserve embedded nulls for validation, and
do not copy or allocate string storage. The String `TryParse...` wrappers are
also explicitly `nothrow`; the `Parse...` wrappers retain their exceptions.

## Counted span extensions

`core/SpanIntegerParsing.gf` supplies `TryParse...` extensions for
`Span<readonly(char)>` at all eleven widths above. The corresponding throwing
extensions live in `core/SpanIntegerParsingThrowing.gf`:

```csharp
using std::Memory;
char[4] buffer = ['x', '1', '2', 'x'];
Span<readonly(char)> text = new(&buffer[1], 2);
long value = text.ParseLong(); // 12; neither surrounding byte is read.
long attempted = 0l;
bool valid = text.TryParseLong(&attempted);
```

These methods copy only span metadata and forward its exact byte count to the
existing parsers. They do not scan for a terminator, copy the owner, or retain a
pointer. An empty span fails normally and writes zero from `TryParse...`, even
when its `Data` is null. Embedded NUL remains invalid decimal text. Successful
and failed overlapping outputs follow the counted pointer rules above. Keep
input and output storage valid for the call; the span does not extend their
lifetimes. The `TryParse...` extensions are `nothrow` and allocate nothing.

The two wrapper files are optional and add no dependency to `Memory/Span.gf` or the
base parsers. Explicit source selections still need their normal Span/query
contracts and `Parsing.gf`/`IntegerParsing.gf`; those base files include throwing
definitions, so selecting the Try wrapper alone does not remove their exception
dependencies. Boolean and hexadecimal parsing are separate APIs.
