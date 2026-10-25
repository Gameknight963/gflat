# JSON pretty print

Write formatted JSON to standard output while preserving property order, duplicate
names, and the exact spelling of quoted strings and numbers. Indentation defaults
to two spaces; an optional ASCII decimal width selects zero through sixteen spaces
(leading zeros and an optional `+` are accepted; minus signs and whitespace are
rejected, following the standard unsigned integer parser).

```powershell
gflat build examples/json-pretty/json-pretty.gfproj -o artifacts/json-pretty.exe
./artifacts/json-pretty.exe path/to/document.json
./artifacts/json-pretty.exe path/to/document.json 4
```

The whole file is held in an owning `String`. `JsonText::PrettyPrint` validates the
entire document before writing anything, then formats its original token bytes
without a DOM or numeric conversion. Indentation width zero still adds line breaks
and a space after each object colon. Empty containers remain inline. A final host
console newline is appended, and stdout is flushed before success. Console text
translation uses the host's newline convention. Input is never modified.

Exit codes are `0` success, `1` parse/file/output error, and `2` incorrect arguments.
Argument validation runs before opening the file. Errors include the input path
and original cause; parse errors also include a zero-based byte offset. Invalid
JSON produces no standard output. After validation, output failure can leave a
prefix: output is not atomic. Filenames use the existing narrow C runtime path API.

See [JSON text operations](../../docs/JSON_TEXT.md) for validation, UTF-8, and
borrowed-input contracts.
