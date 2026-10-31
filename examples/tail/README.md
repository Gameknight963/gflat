# Print the last lines of a file

`tail <file> [count]` prints the last `count` lines in their original order. The default
is 10. The count is a decimal integer in the platform's `nuint` range. An optional
leading `+` is accepted. Minus signs, whitespace, separators, and trailing characters
are rejected.

```powershell
gflat build examples/tail/tail.gfproj -o artifacts/tail.exe
./artifacts/tail.exe application.log 20
```

## Lines

A count of zero opens and closes the file without reading its contents or writing any
lines.

`File.Lines()` recognizes LF and CRLF, retaining bare CR and embedded NUL bytes. The
counted console write preserves embedded NUL bytes and adds a platform newline after
each output line, including an unterminated final input line. Console output uses the
CRT stream mode: newlines are physically CRLF on Windows and LF on Linux. Blank lines
count as lines. Empty input produces no output. The input file is opened in binary mode,
so a Ctrl+Z byte remains input data.

## Storage

Each input line is copied from the iterator's reusable buffer into an owning `String`. A
`Queue<String>` retains at most `count` lines, dropping the oldest before adding
another. A single line can be arbitrarily large within allocation limits, and the file
iterator and current loop value also need temporary line storage. The queue's allocation
can include spare capacity. Output dequeues each owned string and releases it after
writing, without another owning copy for printing.

## Errors

Invalid arguments exit `2`. File, allocation, and output failures exit `1`. Success
exits `0`. File input is consumed and closed before output begins, so an input failure
produces no stdout. An output failure can leave a prefix.

See [file line sequences](../../docs/FILE_LINE_SEQUENCE.md) and
[queues](../../Standard/core/Collections/OWNING_COLLECTIONS.md#queue-and-stack).
