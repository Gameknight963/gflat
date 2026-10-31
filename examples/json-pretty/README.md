# Format JSON

Indent a JSON document while preserving property order, duplicate names, and
the original spelling of strings and numbers:

```powershell
gflat build examples/json-pretty/json-pretty.gfproj -o artifacts/json-pretty.exe
./artifacts/json-pretty.exe path/to/document.json
./artifacts/json-pretty.exe path/to/document.json 4
```

Indentation defaults to two spaces. The optional width accepts zero through
sixteen. Width zero still adds line breaks and a space after object colons.
Empty containers stay inline. Output ends with a platform newline.

The tool loads and validates the complete document before writing. Errors go
to stderr with the input path. Parse errors include a byte offset.

Exit codes are 0 for success, 1 for parse or IO errors, and 2 for invalid
arguments. Output failures can leave partial JSON on stdout.

See [JSON text operations](../../docs/JSON_TEXT.md) for the library API.
