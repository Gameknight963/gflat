# JSON check

Validate one UTF-8 JSON file and report its token count and maximum container nesting. A
primitive root has nesting zero. An empty root array or object has nesting one. Property
names count as tokens. Commas and colons are punctuation.

```powershell
gflat build examples/json-check/json-check.gfproj -o artifacts/json-check.exe
./artifacts/json-check.exe path/to/document.json
./artifacts/json-check.exe --max-bytes 1048576 path/to/document.json
```

## Input and limits

The tool loads the whole file with `FileText::ReadAll`, then keeps that owning `String`
alive while `JsonReader` borrows its bytes. It drains the reader to completion before
printing success.

`--max-bytes count` limits the accepted input byte length. An oversized file is rejected
before parsing or printing success. The count is an unsigned decimal integer that fits
`nuint`. Without this option, the tool reads the complete file. Memory use also includes
String spare capacity, its terminator, and libc buffering. A zero limit accepts only an
empty file, which then fails JSON validation as incomplete input.

## Errors

Exit codes: `0` valid, `1` file or parse error, `2` incorrect arguments. Errors go to
standard error with the input path. Parse errors also include a zero-based byte offset
and reason. See [JSON reader](../../docs/JSON_READER.md) for strict UTF-8, surrogate and
BOM policy.
