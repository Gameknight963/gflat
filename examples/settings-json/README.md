# Convert simple settings to JSON

```powershell
gflat build examples/settings-json/settings-json.gfproj
' title = Hello=world', 'empty =', 'language = gflat' | ./examples/settings-json/bin/Debug/settings-json.exe
```

The program reads standard input until EOF, then writes a JSON object. Keys and
values are strings. Each nonblank line is `key=value`, split at the first `=`.
ASCII whitespace around the key and value is trimmed; whitespace inside them is
preserved. A line whose first nonwhitespace byte is `#` is a comment. Inline `#`,
backslashes, and quotes are ordinary data: there is no quoting or escape syntax.
This is a small line format, not an INI or dotenv parser.

Keys must be nonempty and unique, with case-sensitive byte comparison. Empty
values are allowed. Accepted keys and values must be valid UTF-8; embedded null
bytes are retained through counted spans and escaped in the JSON output. A UTF-8
BOM has no special meaning. Output property order is unspecified.

Each physical line has a 4095-byte payload limit after the host console stream's
newline translation. CRLF and LF delimit lines; see the
[console text-mode rules](../../Standard/libc/README.md). Overlong lines, malformed
settings, duplicates, and invalid UTF-8 return 2 with a line diagnostic on stderr.
No JSON is emitted until all input has passed validation. Allocation or I/O
failures return 1; an output failure can leave partial JSON. Successful output
ends with a newline and returns 0.

Parsing borrows slices of a reusable stack buffer. Only accepted keys and values
are copied into an owning `HashMap<String, String>`; no borrowed input survives
the next read. The map retains all accepted settings until output is complete,
so total memory use grows with the input. No GC is used. The example combines
`TrySplitOnce`, span trimming, UTF-8 validation, map ownership, and `JsonWriter`.
