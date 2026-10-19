# Lexical paths

`PathStyle.gf` and `Paths.gf` provide `std::PathStyle` and `std::Paths`.
They require the core `String` facilities and do not call libc or inspect the
filesystem. Every operation takes an explicit `PathStyle::Unix` or
`PathStyle::Windows`, so a program can manipulate paths from either platform
regardless of where it runs.

Inputs are null-terminated strings. Results are independent owning `String`
values. Bytes are preserved: there is no Unicode conversion, case folding,
validation, existence check, or resolution of `.` and `..`.

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

Windows device paths (`\\?\` and `\\.\`) and alternate data streams are not
parsed specially. These helpers are not a security boundary: lexical joining
does not confine a path to a directory. They do not select a host-native style
or perform filesystem operations; use the libc `File` APIs for actual files.
