# json-get

Print a JSON value selected by a counted RFC 6901 plain pointer, followed by a newline:

```sh
gflat build examples/json-get/json-get.gfproj
json-get document.json /users/0/name
json-get document.json ''
json-get document.json --pointer-file pointer.txt
```

The empty argument selects the root value. Quote arguments as required by your
shell; it must preserve the empty argument. Use `~1` for `/` inside a property name
and `~0` for `~`. URI fragments such as `#/users` are not supported. Array indexes
have no leading zeros; `-` does not select a value.

`--pointer-file` reads the exact counted UTF-8 pointer bytes, including embedded
NUL. No newline trimming or BOM removal is performed. This mode also avoids the
Windows native narrow-argument encoding limit for Unicode property names.
Filenames use the host's narrow-character file APIs, as in the other file tools.

Strings remain quoted and escaped, containers remain complete, and numbers keep
their spelling. Output uses the console's normal text mode, which can translate
newline bytes on Windows. The entire document is validated before any output,
including content after the selected value.

Exit status is 0 on success, 2 for usage or an unresolved pointer (including malformed pointer syntax and ambiguous duplicate names), and 1 for malformed JSON, file errors, or output errors. JSON diagnostics report the original document byte offset on stderr. The input file is unchanged.

The tool loads the complete document and, in file mode, the complete pointer.
The selected span borrows the owning document String until output completes.
Lookup uses [JsonPointer](../../docs/JSON_POINTER.md), with no DOM or owning
copy of the selected value. Reader stacks and decoded path segments can allocate.
