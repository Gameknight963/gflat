# Check JSON

Validate a UTF-8 JSON file and report its token count and maximum nesting:

```powershell
gflat build examples/json-check/json-check.gfproj -o artifacts/json-check.exe
./artifacts/json-check.exe path/to/document.json
./artifacts/json-check.exe --max-bytes 1048576 path/to/document.json
```

A primitive root has nesting zero. An empty array or object has nesting one.
Property names count as tokens, while commas and colons are punctuation.

`--max-bytes count` rejects files larger than the given unsigned decimal byte
count. Otherwise the complete file is loaded. Memory use also includes String
capacity and runtime buffers.

Exit codes are 0 for valid JSON, 1 for file or parse errors, and 2 for invalid
arguments. Errors go to stderr with the path. Parse errors include a zero-based
byte offset and reason.

See [JsonReader](../../docs/JSON_READER.md) for the accepted JSON profile.
