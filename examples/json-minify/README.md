# JSON minify

Write compact JSON to standard output, preserving property order, duplicate names,
and the exact spelling of strings and numbers, including original escapes and
numbers outside machine numeric ranges. One final host console newline is appended.

```powershell
gflat build examples/json-minify/json-minify.gfproj -o artifacts/json-minify.exe
./artifacts/json-minify.exe path/to/document.json
```

The complete file is held in an owning `String`; `JsonText::Minify` borrows those
bytes. It validates the **entire** document before any output is written, then
forwards contiguous runs while removing JSON whitespace outside strings. There
is no DOM, string scratch buffer, number conversion, or chunked input protocol.
Input is never modified.

Exit codes are `0` success, `1` parse/file/output error, and `2` incorrect arguments.
Errors go to standard error with the input path and, for parse errors, a zero-based
byte offset and reason. Invalid input produces no standard output. After validation
passes, output failures can leave partial output; writes are not an
atomic transaction. The tool flushes stdout before reporting success.

See [JSON text operations](../../docs/JSON_TEXT.md) for the borrowing contract and
[JSON reader](../../docs/JSON_READER.md) for strict UTF-8 and escape policies.
