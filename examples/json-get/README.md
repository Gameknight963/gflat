# Select a JSON value

Print the value at a JSON Pointer path:

```sh
gflat build examples/json-get/json-get.gfproj
json-get document.json /users/0/name
json-get document.json ''
json-get document.json --pointer-file pointer.txt
```

An empty pointer selects the root. In a property name, `~1` represents `/` and
`~0` represents `~`. Array indexes are decimal integers without leading zeroes.
Quote arguments as required by your shell, preserving empty arguments.

`--pointer-file` reads exact UTF-8 pointer bytes from a file, including any
newline, BOM, or embedded NUL. It also supports Unicode names that the Windows
narrow-character command line cannot represent.

The entire document is validated before output. The selected value retains its
original spelling, including string quotes and escapes. The tool holds the
complete document and pointer in memory. See [JsonPointer](../../docs/JSON_POINTER.md).

Exit codes are 0 for success, 2 for invalid arguments or an unresolved pointer,
and 1 for invalid JSON or IO failure. Malformed pointer syntax and ambiguous
duplicate names count as unresolved pointers. Errors go to stderr, with byte
offsets for JSON errors.
