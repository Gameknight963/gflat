# Lexical paths

`PathStyle.gf` and `Paths.gf` provide `std::PathStyle` and `std::Paths`.
They require the core `String` facilities and do not call libc or inspect the
filesystem. Every operation takes an explicit `PathStyle::Unix` or
`PathStyle::Windows`, so a program can manipulate paths from either platform
regardless of where it runs.

The pointer overloads take null-terminated strings. Their results are independent
owning `String` values. Bytes are preserved: there is no Unicode conversion, case folding,
validation, existence check, or resolution of `.` and `..`.

The five `Get...` operations also accept `Span<readonly(char)>`, returning a
borrowed `Span<readonly(char)>` instead of allocating a `String`. They use the
same lexical rules below, read only the supplied count, and treat embedded null
and high bytes as ordinary path bytes. Empty results have null `Data` and zero
length. No terminator is appended or promised. Keep the original storage alive
and do not relocate it while using a result; edits through another mutable view
remain visible. These views protect characters but do not freeze their owner.

```csharp
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
form below; embedded null and high bytes are preserved, and the result receives
a trailing null outside its `Length`. Empty views do not access their storage.
Keep input storage readable and unchanged during the call; no input pointer is
retained afterward. Allocation failure preserves both inputs and an existing
assignment destination. Empty input on both sides requires no result allocation.
These lexical byte operations do not make counted null bytes valid filenames
for a filesystem API.

Explicit source selections also need `Span.gf`, `Collections/IEnumerable.gf`,
and `Collections/Enumerable.gf` for these overload declarations. Ordinary
projects including the standard library already include these sources.

- `GetFileName(path, style)` returns the final component. A trailing separator
  produces an empty filename. Windows drive prefixes and UNC roots are excluded.
- `GetDirectoryName(path, style)` returns the containing directory, preserving a
  root when appropriate. Roots and paths without a directory return an empty
  string. For `dir/`, the result is `dir`; it does not query whether `dir` exists.
- `GetExtension(path, style)` uses the last dot in the filename, including the
  dot. A leading dot counts (`.config` has extension `.config`); a final dot
  produces an empty extension. Dots in directory components are ignored.
- `GetFileNameWithoutExtension(path, style)` removes that final dot and suffix.
- `GetRoot(path, style)` returns `/` for a Unix absolute path; Windows paths can
  have drive roots (`C:\`), drive-relative prefixes (`C:`), rooted separators,
  or UNC server/share roots. A root's existing trailing separator is preserved.
- `Join(left, right, style)` concatenates two components. It inserts `/` for
  Unix or `\` for Windows when neither boundary has a separator, and removes
  one duplicated separator when both do. Other separators and dot segments are
  preserved. An empty component returns a copy of the other. A rooted right
  component does **not** discard the left component.
- `IsSeparator(character, style)` recognizes `/` for Unix; Windows additionally
  recognizes `\`.
- `IsRooted(path, style)` recognizes a leading separator or a Windows drive
  prefix. `C:child` and `\child` are rooted but still depend on the current
  directory of a drive or the current drive, respectively.
- `IsFullyQualified(path, style)` distinguishes paths independent of those
  defaults: a leading `/` for Unix; two leading separators or a drive prefix
  followed by a separator for Windows. These are lexical root checks, not name
  validation: `\\` alone qualifies even though it is not a complete UNC share.
  Both classification helpers and `IsSeparator` are `nothrow` and allocate
  nothing. Their pointer overloads inspect null-terminated input without
  copying it.

Windows device paths (`\\?\` and `\\.\`) and alternate data streams are not
parsed specially. These helpers are not a security boundary: lexical joining
does not confine a path to a directory. They do not select a host-native style
or perform filesystem operations; use the libc `File` APIs for actual files.
