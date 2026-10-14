# Line search

A small binary-file reader that prints lines containing a literal byte substring.
It supports LF, CRLF, and a final line without a newline. A bare CR remains data.
Strings grow for long
lines; matching is case-sensitive and does not interpret regular expressions.

```text
gflat build examples/search/search.gfproj --run -- std/libc/File.gf IOException
```

You can also build without `--run`, then invoke
`examples/search/bin/Debug/search.exe` directly (prefix it with `./` on Linux).
The `.exe` suffix is the current project output convention on both platforms.
Exit codes: zero after a successful search, one on file IO failure, two for usage.
An empty search string matches every line. Output uses Console's text-stream
newline handling; this tool does not preserve original line endings.
Paths and command-line arguments use the platform's narrow-character encoding;
this is not a Unicode filename/argument API on Windows. Matching compares bytes.
