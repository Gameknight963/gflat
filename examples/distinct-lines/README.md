# Distinct lines

Write the first occurrence of each stdin line, keeping the original order:

```powershell
gflat build examples/distinct-lines/distinct-lines.gfproj -o artifacts/distinct-lines.exe
"pear", "apple", "pear", "", "apple" | ./artifacts/distinct-lines.exe
```

This prints `pear`, `apple`, and one empty line. New lines are written as they
arrive. Comparison is case-sensitive and uses the complete line bytes, including
embedded NULs.

LF and CRLF end lines. A bare CR remains data, and a final unterminated line is
included. Each result gets a platform newline. On Windows, stdin text mode
translates CRLF and treats Ctrl+Z as EOF.

The hash set retains every distinct line until the command finishes. Memory use
grows with the number and size of those lines. The example uses [HashSet](../../docs/HASH_COLLECTIONS.md)
and [line enumeration](../../docs/BYTE_READERS.md#enumerate-lines-without-owning-the-source).

The command takes no arguments. Exit codes are 0 for success, 2 for arguments,
and 1 for IO or allocation failure. Errors go to stderr. Failures can leave
previously written lines on stdout.
