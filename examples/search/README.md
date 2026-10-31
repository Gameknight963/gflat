# Line search

A small binary-file reader that prints lines containing a literal byte substring. It
supports LF, CRLF, and a final line without a newline. A bare CR remains data. Strings
grow for long lines. Matching is case-sensitive by default. Matching uses the standard
String substring search. Removing a CRLF ending reuses the line's buffer instead of
constructing another owning string.

```text
gflat build examples/search/search.gfproj --run -- Standard/libc/IO/File.gf IOException
```

You can also build without `--run`, then invoke `examples/search/bin/Debug/search.exe`
directly (prefix it with `./` on Linux). The `.exe` suffix is the current project output
convention on both platforms. Exit codes: zero after a successful search and output
flush, one on input or output IO failure, two for usage. Matching lines go to stdout.
Usage and failure diagnostics go to stderr. Output failures are reported separately from
input failures. Since results stream while reading, an error can leave partial output.
An empty search string matches every line. Output uses Console's text-stream newline
handling. Paths and command-line arguments use the platform's narrow-character encoding.

## ASCII case-insensitive matching

Pass `--ignore-case` before the file to fold ASCII letters while preserving the original
bytes in matching output:

```text
gflat build examples/search/search.gfproj --run -- --ignore-case input.txt needle
```

Only ASCII `a`-`z` and `A`-`Z` are equivalent. Non-ASCII bytes remain distinct. Embedded
nulls in file lines remain ordinary bytes. The command-line search text is
null-terminated.

This mode uses the counted `Ascii::IndexOfIgnoreCase` search without allocating a folded
copy. Its worst-case matching work is O(line length * pattern length).
