# Convert settings to JSON

Read `key=value` lines from stdin and write a JSON object:

```powershell
gflat build examples/settings-json/settings-json.gfproj
' title = Hello=world', 'empty =', 'language = gflat' | ./examples/settings-json/bin/Debug/settings-json.exe
```

Lines split at the first `=`. Surrounding ASCII whitespace is trimmed from keys
and values. A first nonwhitespace `#` starts a comment. Blank lines are ignored.
Quotes, backslashes, and later `#` characters are ordinary data.

Keys must be nonempty and unique. Keys and values must be valid UTF-8. Empty
values and embedded NULs are preserved in the JSON strings. Property order is
unspecified.

Each line can contain at most 4095 bytes, excluding LF or CRLF. The map retains
all accepted settings until output, so memory use grows with input size.

All input is validated before JSON is written. Exit codes are 0 for success,
2 for invalid or oversized input, and 1 for IO or allocation failure. Input
errors go to stderr with a line number. Output failures can leave partial JSON.
