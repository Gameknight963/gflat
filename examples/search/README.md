# Line search

A small binary-file reader that prints lines containing a literal byte substring.
It supports LF, CRLF, and a final line without a newline. A bare CR remains data.
The standard file-line cursor reuses a String buffer, growing it for long lines.
Matching and output borrow the current line without copying it. Matching uses
the standard String substring search, is case-sensitive, and does not interpret
regular expressions.

```text
gflat build examples/search/search.gfproj --run -- std/libc/File.gf IOException
```

You can also build without `--run`, then invoke
`examples/search/bin/Debug/search.exe` directly (prefix it with `./` on Linux).
The `.exe` suffix is the current project output convention on both platforms.
Exit codes: zero after a successful search and output flush, one on input or
output IO failure, two for usage. Matching lines go to stdout; usage and failure
diagnostics go to stderr. Output failures are reported separately from input
failures. Since results stream while reading, an error can leave partial output.
An empty search string matches every line. Output uses Console's text-stream
newline handling; this tool does not preserve original line endings.
Paths and command-line arguments use the platform's narrow-character encoding;
this is not a Unicode filename/argument API on Windows. Matching compares bytes.
