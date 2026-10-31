# JSON minify

Write compact JSON to standard output, preserving property order, duplicate names, and
the exact spelling of strings and numbers, including original escapes and numbers
outside machine numeric ranges. One final host console newline is appended.

```powershell
gflat build examples/json-minify/json-minify.gfproj -o artifacts/json-minify.exe
./artifacts/json-minify.exe path/to/document.json
```

## Processing

The complete file is held in an owning `String`. `JsonText::Minify` validates the
document before writing, then copies token bytes while removing whitespace outside
strings.

## Errors

Exit codes are `0` success, `1` parse/file/output error, and `2` incorrect arguments.
Errors go to standard error with the input path and, for parse errors, a zero-based byte
offset and reason. Invalid input produces no standard output. After validation passes,
output failures can leave partial output. The tool flushes stdout before reporting
success.

See [JSON text operations](../../docs/JSON_TEXT.md) for the borrowing contract and [JSON
reader](../../docs/JSON_READER.md) for strict UTF-8 and escape policies.
