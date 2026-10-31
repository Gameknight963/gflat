# Lexical paths

`PathStyle.gf` and `Paths.gf` provide `Standard::IO::PathStyle` and `Standard::IO::Paths`.
They use core `String` facilities for lexical path operations. Every operation takes an explicit `PathStyle::Unix` or
`PathStyle::Windows`, so a program can manipulate paths from either platform
regardless of where it runs.

The pointer overloads take null-terminated strings. Path-producing pointer overloads
return independent owning `String` values. Operations preserve bytes, case, and
dot segments. Filesystem resolution happens when those paths are used.

The five `Get...` operations also accept `Span<readonly(char)>`, returning a
borrowed `Span<readonly(char)>` instead of allocating a `String`. They use the
same lexical rules below, read only the supplied count, and treat embedded null
and high bytes as ordinary path bytes. Empty results have null `Data` and zero
length. No terminator is appended or promised. Keep the original storage alive
and do not relocate it while using a result. Edits through another mutable view
remain visible. These views protect characters but do not freeze their owner.

```csharp
using Standard::Text;
using Standard::IO;
using Standard::Memory;
String path = s"assets/settings.json";
Span<readonly(char)> name = Paths::GetFileName(path.AsReadOnly(), PathStyle::Unix);
// name borrows "settings.json" from path.
```

`IsRooted` and `IsFullyQualified` also accept counted spans. They read only the
root prefix inside the view, allocate nothing, and are `nothrow`. Default and
zero-length spans return false without reading storage. Embedded null and high
bytes remain ordinary bytes, so a counted `C`, null, `/` is not a drive root.
The C-string forms still inspect only a bounded prefix rather than scanning the
whole path.

`Join(leftSpan, rightSpan, style)` accepts two counted views and returns an
independent owning `String`. It uses the same boundary rules as the C-string
form below. Embedded null and high bytes are preserved, and the result receives
a trailing null outside its `Length`. Empty views do not access their storage.
Keep input storage readable and unchanged during the call. No input pointer is
retained afterward. Allocation failure preserves both inputs and an existing
assignment destination. Empty input on both sides requires no result allocation.
A filesystem call still requires a path valid under that filesystem's rules.

Explicit source selections also need `Memory/Span.gf`, `Collections/IEnumerable.gf`,
and `Queries/Enumerable.gf` for these overload declarations. Ordinary
projects including the standard library already include these sources.

## Components and extensions

- `GetFileName(path, style)` returns the final component. A trailing separator
  produces an empty filename. Windows drive prefixes and UNC roots are excluded.
- `GetDirectoryName(path, style)` returns the containing directory, preserving a
  root when appropriate. Roots and paths without a directory return an empty
  string. For `dir/`, the result is `dir`. It does not query whether `dir` exists.
- `GetExtension(path, style)` uses the last dot in the filename, including the
  dot. A leading dot counts (`.config` has extension `.config`). A final dot
  produces an empty extension. Dots in directory components are ignored.
- `GetFileNameWithoutExtension(path, style)` removes that final dot and suffix

### Changing extensions

- `ChangeExtension(path, extension, style)` replaces only the final filename's last
  dot and suffix, preserving all preceding directory/root bytes. It follows the
  same leading-dot and final-dot policy as `GetExtension`: `.config` with `json`
  becomes `.json`, and `file.` with `txt` becomes `file.txt`.
  A nonempty replacement gets a leading `.` unless it already starts with one.
  `.` requests a trailing dot, while an empty replacement removes the dot and
  suffix. With no existing extension, a nonempty replacement is appended.
  Empty paths and paths with no filename (roots, drive-only prefixes and trailing
  separators) return an unchanged independent copy. No filename is invented.
  Both the C-string pair and counted-span pair return an owning `String`. Counted
  inputs preserve embedded null/high bytes and never read outside their views.
  Empty ranges need no storage. The result is null-terminated outside its length
  and independent even when the two inputs overlap or borrow the assignment
  destination's old storage. Keep inputs readable/stable during the call. The
  complete result length, including space for String's terminator, is checked
  before result allocation. Allocation or length failure preserves the inputs
  and an existing assignment destination. The same explicit style rules apply on every host.

## Roots and separators

- `GetRoot(path, style)` returns `/` for a Unix absolute path. Windows paths can
  have drive roots (`C:\`), drive-relative prefixes (`C:`), rooted separators,
  or UNC server/share roots. A root's existing trailing separator is preserved.
- `EndsInDirectorySeparator(path, style)` checks the final byte, returning false
  for an empty path. Both C-string and counted-span overloads are `nothrow` and
  allocate nothing.
- `TrimEndingDirectorySeparator(path, style)` removes exactly one trailing
  separator beyond the root, using the existing `GetRoot` rules. Unix `/` stays
  `/`, `dir//` becomes `dir/`, and `//` becomes `/`. Windows `C:\` stays `C:\`.
  All other bytes remain unchanged. The C-string overload returns an independent
  owning String. The counted-span overload borrows the same starting address for
  a nonempty result and returns a canonical empty span for empty input. The span
  overload allocates nothing, reads no terminator, and preserves counted NUL and
  high bytes. Keep its input owner alive and stable while using the result.
  Existing lexical UNC roots, including incomplete spellings, remain roots.
  These methods add no special parsing for Windows device prefixes. The operation
  follows the single-separator contract of
  [Path.TrimEndingDirectorySeparator](https://learn.microsoft.com/en-us/dotnet/api/system.io.path.trimendingdirectoryseparator),
  with this library's explicit styles and root rules.

## Joining and classification

- `Join(left, right, style)` concatenates two components. It inserts `/` for
  Unix or `\` for Windows when neither boundary has a separator, and removes
  one duplicated separator when both do. Other separators and dot segments are
  preserved. An empty component returns a copy of the other. A rooted right
  component does **not** discard the left component.
- `IsSeparator(character, style)` recognizes `/` for Unix. Windows additionally
  recognizes `\`.
- `IsRooted(path, style)` recognizes a leading separator or a Windows drive
  prefix. `C:child` and `\child` are rooted but still depend on the current
  directory of a drive or the current drive, respectively
- `IsFullyQualified(path, style)` distinguishes paths independent of those
  defaults: a leading `/` for Unix, or two leading separators or a drive prefix
  followed by a separator for Windows. These are lexical root checks, not name
  validation: `\\` alone qualifies even though it is not a complete UNC share.
  Both classification helpers and `IsSeparator` are `nothrow` and allocate
  nothing. Their pointer overloads inspect null-terminated input without
  copying it.

Windows device prefixes (`\\?\` and `\\.\`) and alternate data streams follow
the ordinary byte rules above. Validate directory confinement separately before
using untrusted paths. Use libc `File` APIs for filesystem access.
