# Decode JSON string lines

```console
gflat build examples/unquote-lines/unquote-lines.gfproj --run -- input.jsonl
```

Each input line must contain exactly one complete UTF-8 JSON string, including
its quotes. Outside whitespace, other JSON values, malformed escapes, unpaired
surrogates, and invalid UTF-8 are rejected. No BOM is removed. LF and CRLF end
input lines; a trailing terminator does not add an empty line. An empty file
produces no output, whereas a blank input line is invalid.

The decoded bytes are written with a newline after each value. Embedded NULs and
escaped newlines are retained, so the output need not have one physical line per
value. Input line endings are normalized to logical LF, not preserved as CRLF.
Console output retains the host's text mode, including LF-to-CRLF translation on
Windows. This is a text conversion tool, not a byte-for-byte file round trip.

One owning String buffer is reused for reading and exact same-start decoding;
there is no second decoded buffer. Invalid input returns 1 and reports its line
number and input path on stderr. Open, read, explicit close, and output flush
failures also return 1 and identify the path without claiming a line position.
Earlier completed output may remain. Incorrect arguments return 2;
success returns 0. The input file is never modified.
