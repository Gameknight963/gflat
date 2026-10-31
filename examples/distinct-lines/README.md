# Distinct lines

Read stdin and write the first occurrence of each counted line, preserving its original
order. It writes each new line as it arrives, using normal stdio buffering.

```powershell
gflat build examples/distinct-lines/distinct-lines.gfproj -o artifacts/distinct-lines.exe
"pear", "apple", "pear", "", "apple" | ./artifacts/distinct-lines.exe
```

## Line identity

This writes `pear`, `apple`, and one empty line. Identity is case-sensitive and uses the
complete counted byte sequence. Embedded NULs, bytes outside ASCII and invalid UTF-8
remain data. For example, `a`, `a\0` and `a\0b` are distinct lines.

LF and CRLF end a line and are removed before lookup. A bare CR remains data. Each
output line gets one host console newline, including a final unterminated input line.
Empty input produces no output. One newline is one empty line, and repeated blank lines
produce a single blank output line.

## Console behavior

The process's stdio text mode remains in effect. Windows translates physical CRLF before
parsing and treats Ctrl+Z as EOF. Thus physical `a\r\r\n` becomes logical `a\r\n` in the
CRT and then the line `a` in the parser. Output newlines use the host text stream's
newline translation. Counted writes preserve embedded NUL and high bytes.

## Storage and borrowing

The BufferedReader borrows stdin and a 4096-byte stack scratch buffer. Keep all
consuming access through that same reader while it retains input. LineSequence borrows
the buffered reader. Its iterator owns one reusable String. The loop takes a temporary
immutable pointer to Current, valid until the next MoveNext, and probes HashSet without
making a String copy. Only an unseen line is copied into the owning HashSet, whose
normal destruction frees all keys.

Memory use is unbounded: the set retains every unique line until completion, plus its
table and the iterator's longest-line buffer. There is no fixed line length limit beyond
allocation and size limits. Many unique or very long lines can exhaust memory.

## Errors

Exit codes are `0` success, `1` an input, allocation or output exception, and `2` for
arguments (none are supported). Errors go to stderr. Since processing is incremental, an
input/allocation/output failure can leave partial output. Stdout is explicitly flushed
before success.
