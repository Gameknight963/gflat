# Minify JSON

Remove whitespace between JSON tokens:

```powershell
gflat build examples/json-minify/json-minify.gfproj -o artifacts/json-minify.exe
./artifacts/json-minify.exe path/to/document.json
```

Property order, duplicate names, string escapes, and number spellings are
preserved. Output ends with a platform newline.

The tool loads and validates the complete document before writing. Invalid JSON
produces an error on stderr with the file path, byte offset, and reason.

Exit codes are 0 for success, 1 for parse or IO errors, and 2 for invalid
arguments. Output failures can leave partial JSON on stdout.

See [JSON text operations](../../docs/JSON_TEXT.md) for the library API.
