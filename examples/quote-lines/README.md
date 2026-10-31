# Quote text lines as JSON

This tool reads a UTF-8 file and writes one JSON string literal per line to stdout. It
is useful for turning line-oriented text into newline-delimited JSON string values,
retaining quotes, backslashes, control characters, and Unicode.

```console
gflat build examples/quote-lines/quote-lines.gfproj --run -- input.txt
```

## Lines

LF and CRLF end input lines and are excluded from their values. A bare CR and embedded
zero bytes remain text and are escaped. Empty input produces no output. An empty
terminated line produces `""`. A trailing line terminator does not create an additional
empty line. A BOM remains part of the first value.

## Errors

Each line must be valid UTF-8. Invalid input or an I/O failure returns status 1 and
reports the exception message on stderr. Incorrect arguments return 2. Successful
conversion returns 0. An error can occur after earlier lines have already been written.
Stdout uses the host console's existing text mode, including its newline translation on
Windows.

## Storage and borrowing

The example combines a borrowed `FileLineSequence`, its checked readonly current line,
and `WriteJsonString` on `Console::Out`. One owning line buffer grows as needed and is
reused. Quoting writes directly to the output writer. Reading `Current` through its
reference avoids a per-line String copy.
