# Distinct lines

Read stdin and write the first occurrence of each counted line, preserving its
original order. Unlike `sort-lines --unique`, this filter does not sort or wait
for all input before it starts writing. Output still follows normal stdio buffering;
it does not flush every line.

```powershell
gflat build examples/distinct-lines/distinct-lines.gfproj -o artifacts/distinct-lines.exe
"pear", "apple", "pear", "", "apple" | ./artifacts/distinct-lines.exe
```

This writes `pear`, `apple`, and one empty line. Identity is case-sensitive and
uses the complete counted byte sequence. Embedded NULs, bytes outside ASCII and
invalid UTF-8 remain data; the tool does not validate or normalize text encoding.
For example, `a`, `a\0` and `a\0b` are distinct lines.

LF and CRLF end a line and are removed before lookup. A bare CR remains data.
Each output line gets one host console newline, including a final unterminated
input line. Empty input produces no output; one newline is one empty line, and
repeated blank lines produce a single blank output line.

The process's stdio text mode remains in effect. Windows translates physical
CRLF before parsing and treats Ctrl+Z as EOF. Thus physical `a\r\r\n` becomes
logical `a\r\n` in the CRT and then the line `a` in the parser. Output newlines
use the host text stream's newline translation. This example does not enable
binary stdio mode; counted writes still preserve embedded NUL and high bytes.

The BufferedReader borrows stdin and a 4096-byte stack scratch buffer. Keep all
consuming access through that same reader while it retains input. LineSequence
borrows the buffered reader and owns no source; its iterator owns one reusable
String. The loop takes a temporary immutable pointer to Current, valid until the
next MoveNext, and probes HashSet without making a String copy. Only an unseen
line is copied into the owning HashSet, whose normal destruction frees all keys.
No garbage collector is needed. Stdin is never closed by the adapter.

Memory use is unbounded: the set retains every unique line until completion,
plus its table and the iterator's longest-line buffer. There is no fixed line
length limit beyond allocation and size limits. Many unique or very long lines
can exhaust memory; this is not an external-memory deduplication tool.

Exit codes are `0` success, `1` an input, allocation or output exception, and `2`
for arguments (none are supported). Errors go to stderr. Since processing is
incremental, an input/allocation/output failure can leave partial output; no
rollback or draining is attempted. Stdout is explicitly flushed before success.
