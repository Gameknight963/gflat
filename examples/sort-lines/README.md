# Sort lines

Read stdin to EOF, sort all lines in ordinal byte order, and write them to stdout.
The default mode preserves empty lines and duplicates. The optional `--unique`
mode writes one line for each exact counted byte sequence, including empty lines.
Lines compare byte by byte as unsigned
values; a shorter matching prefix comes first. Comparison is case-sensitive and
independent of locale, Unicode collation, and null termination.

```powershell
gflat build examples/sort-lines/sort-lines.gfproj -o artifacts/sort-lines.exe
"pear", "apple", "pear", "" | ./artifacts/sort-lines.exe
"pear", "apple", "pear", "" | ./artifacts/sort-lines.exe --unique
```

The tool holds every line in a `Vector<String>`, so memory use grows with the
complete input. Each read String is moved into the vector, and the input local
is reinitialized for the next line. The default `Sort` overload moves those owners
in place through `IComparable<String>` and `String.CompareTo`; it creates no copies
of their byte buffers. The algorithm is unstable, which does not affect the output
because equal lines are identical. With `--unique`, the output pass compares
adjacent sorted Strings by their full counted contents and writes the first of
each equal run. Embedded NULs and bytes outside ASCII participate in identity;
case differences remain distinct. Every input line is still retained until the
owning vector is destroyed, so unique mode has the same full-input memory use.
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

The only accepted forms are no arguments or the single argument `--unique`.
Unknown or extra arguments print `Usage: sort-lines [--unique]` and return `2`
before reading stdin.

Exit codes are `0` success, `1` an input, allocation, or output exception, and `2`
for invalid arguments.
Errors are written to stderr. Reading finishes before output begins, so input
failures produce no sorted output. Output failures can leave partial output.
Stdout is flushed before reporting success.
