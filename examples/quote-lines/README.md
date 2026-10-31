# Quote text lines as JSON

Write one JSON string per line of a UTF-8 file:

```console
gflat build examples/quote-lines/quote-lines.gfproj --run -- input.txt
```

LF and CRLF delimit input lines. A bare CR or embedded NUL belongs to the line
and is escaped in JSON. Empty lines become `""`, and a final unterminated line
is included. A BOM remains part of the first value.

The example combines [file line iteration](../../docs/FILE_LINE_SEQUENCE.md)
with [JSON string formatting](../../docs/JSON_STRING.md), reusing one line buffer.
Memory use grows with the longest line.

Exit codes are 0 for success, 2 for invalid arguments, and 1 for invalid UTF-8
or IO failure. Errors go to stderr. Earlier lines may already have been written.
