# JSON minify

Write compact JSON to standard output, preserving property order, duplicate names,
and the exact spelling of numbers, including numbers outside machine numeric ranges.
Strings are decoded and escaped again, so their escape spelling can change without
changing their decoded contents. One final host console newline is appended.

```powershell
gflat build examples/json-minify/json-minify.gfproj -o artifacts/json-minify.exe
./artifacts/json-minify.exe path/to/document.json
```

The complete file is held in an owning `String`; `JsonReader` borrows those bytes.
The first pass validates the **entire** document before any output is written.
The second pass forwards tokens to `JsonWriter(Console::Out)`, reusing one owned
scratch string to decode property names and string values in place. There is no
DOM, number conversion, or chunked input protocol. Input is never modified.

Exit codes are `0` success, `1` parse/file/output error, and `2` incorrect arguments.
Errors go to standard error with the input path and, for parse errors, a zero-based
byte offset and reason. Invalid input produces no standard output. After validation
passes, allocation or output failures can leave partial output; writes are not an
atomic transaction. The tool flushes stdout before reporting success.

The [JSON reader](../../docs/JSON_READER.md) and
[JSON writer](../../docs/JSON_WRITER.md) document strict UTF-8 and escape policies.
