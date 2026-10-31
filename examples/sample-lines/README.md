# Sample file lines

`sample-lines <input-file> <count> [seed]` picks up to `count` lines without
replacement, then prints them in randomized order:

```powershell
gflat build examples/sample-lines/sample-lines.gfproj -o artifacts/sample-lines.exe
./artifacts/sample-lines.exe application.log 20
./artifacts/sample-lines.exe application.log 20 42
```

Count is an unsigned pointer-sized decimal integer. Seed is an unsigned 64-bit
decimal integer and defaults to zero. The same input, count, and seed produce
the same sample. Duplicate line contents remain separate input positions.
A count of zero returns immediately after validating the arguments.

LF and CRLF delimit lines. Bare CR, embedded NUL, and Ctrl+Z remain data.
Selected lines are written with platform newlines.

Reservoir sampling retains at most `count` lines during one forward pass. Memory
use depends on their byte lengths and the longest line read. Sampling finishes
before output begins.

Exit codes are 0 for success, 2 for invalid arguments, and 1 for file,
allocation, or output failure. Errors go to stderr.
