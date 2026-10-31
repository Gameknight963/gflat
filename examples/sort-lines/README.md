# Sort lines

Sort stdin lines in byte order, optionally keeping only unique lines:

```powershell
gflat build examples/sort-lines/sort-lines.gfproj -o artifacts/sort-lines.exe
"pear", "apple", "pear", "" | ./artifacts/sort-lines.exe
"pear", "apple", "pear", "" | ./artifacts/sort-lines.exe --unique
```

Comparison is case-sensitive and uses unsigned byte values. A shorter matching
prefix comes first. Embedded NULs and bytes outside ASCII participate in
comparison. `--unique` writes one copy of each distinct line.

LF and CRLF delimit lines. Bare CR remains data, and a final unterminated line
is included. Results use platform newlines. Windows stdin text mode translates
CRLF and treats Ctrl+Z as EOF.

All lines are kept in a `Vector<String>` until sorting finishes. Memory use
grows with the complete input, including when `--unique` is used.

Exit codes are 0 for success, 2 for invalid arguments, and 1 for IO or
allocation failure. Errors go to stderr. Reading finishes before output starts.
