# Print a file's last lines

`tail <file> [count]` prints up to the last `count` lines in their original
order. The default is 10:

```powershell
gflat build examples/tail/tail.gfproj -o artifacts/tail.exe
./artifacts/tail.exe application.log 20
```

Count is an unsigned pointer-sized decimal integer, optionally prefixed with
`+`. A count of zero opens and closes the file without reading its contents.

LF and CRLF delimit lines. Bare CR and embedded NULs remain data, and a final
unterminated line is included. Results use platform newlines.

A `Queue<String>` retains at most `count` lines. Memory use depends on their
byte lengths and the longest line read. The complete input is consumed and
closed before output starts.

Exit codes are 0 for success, 2 for invalid arguments, and 1 for IO or
allocation failure. Errors go to stderr.

See [file lines](../../docs/FILE_LINE_SEQUENCE.md) and [owning collections](../../Standard/core/Collections/OWNING_COLLECTIONS.md).
