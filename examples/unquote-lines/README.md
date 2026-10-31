# Decode JSON string lines

Read one JSON string literal per input line and write its decoded value:

```console
gflat build examples/unquote-lines/unquote-lines.gfproj --run -- input.jsonl
```

Each line must contain exactly one complete UTF-8 JSON string, including its
quotes. Outside whitespace and other JSON values are rejected. LF and CRLF
delimit lines. An empty file succeeds, while a blank line is invalid.

Each decoded value is followed by a newline. Embedded NULs and escaped newlines
are retained, so a value can span several output lines. Windows console output
translates LF to CRLF.

The input buffer is reused for in-place decoding. Memory use grows with the
longest line. See [JSON string decoding](../../docs/JSON_STRING.md).

Exit codes are 0 for success, 2 for invalid arguments, and 1 for invalid input
or IO failure. Errors go to stderr with the path and, for invalid lines, a line
number. Earlier decoded values may already have been written.
