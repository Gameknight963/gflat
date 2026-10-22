# Sort lines

Read stdin to EOF, sort all lines in ordinal byte order, and write them to stdout.
Empty lines and duplicates are preserved. Lines compare byte by byte as unsigned
values; a shorter matching prefix comes first. Comparison is case-sensitive and
independent of locale, Unicode collation, and null termination.

```powershell
gflat build examples/sort-lines/sort-lines.gfproj -o artifacts/sort-lines.exe
"pear", "apple", "pear", "" | ./artifacts/sort-lines.exe
```

The tool holds every line in a `Vector<String>`, so memory use grows with the
complete input. Each read String is moved into the vector, and the input local
is reinitialized for the next line. `Sort` moves those owners in place using
`String.CompareTo`; it creates no copies of their byte buffers. The algorithm
is unstable, which does not affect the output because equal lines are identical.
No collector or external sorting is used.

`Console::TryReadLine` removes LF/CRLF endings from the host text-stream bytes
and accepts a final unterminated line. A bare CR in that stream remains data.
Counted writes preserve embedded zero bytes and
append one host console newline to every output line, including the final line.
There is no fixed line-length limit beyond the library's allocation and size
limits. An empty stream produces no lines; a single newline represents one empty
line.

The host stdio mode remains in effect. In particular, Windows console text mode
translates CRLF before the library processes lines and treats Ctrl+Z as EOF.
For example, physical `a\r\r\n` becomes `a\r\n` in the CRT and then the line
`a` in the reader. This tool does not select binary mode.
The ordinary stdio and exception runtime buffering/allocation behavior applies.

Exit codes are `0` success and `1` an input, allocation, or output exception.
Errors are written to stderr. Reading finishes before output begins, so input
failures produce no sorted output. Output failures can leave partial output.
Stdout is flushed before reporting success.
