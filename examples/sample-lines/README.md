# Sample lines from a file

`sample-lines <input-file> <count> [seed]` selects up to `count` lines without
replacement and prints them in randomized order. The seed defaults to zero. The same
input, count, and seed produce the same output. Duplicate line contents remain separate
input positions and can appear repeatedly in a sample.

```powershell
gflat build examples/sample-lines/sample-lines.gfproj -o artifacts/sample-lines.exe
./artifacts/sample-lines.exe application.log 20
./artifacts/sample-lines.exe application.log 20 42
```

## Arguments

Count is a decimal integer in the platform's `nuint` range. Seed is an unsigned 64-bit
decimal integer. Both accept an optional `+` and leading zeros. Minus signs (including
`-0`), whitespace, separators, and trailing characters are rejected. Both arguments are
validated before accessing the file. Count zero returns success without opening or
reading the path, generating values, writing output, or flushing stdout. An invalid seed
still fails when count is zero.

## Sampling

The tool performs one forward pass using reservoir sampling. It initially retains the
first `count` positions. Each later line draws uniformly from all positions seen so far
and replaces that reservoir slot if the draw falls below count. The line counter is
checked before incrementing. Finally, Fisher-Yates `Shuffle` uses the same
`PseudoRandom` state to randomize the retained lines, including when the file has fewer
lines than requested. The generator is deterministic and noncryptographic.

A `Vector<String>` holds O(min(count, input lines)) owned lines, with ordinary geometric
spare capacity. The file cursor has one reusable current-line buffer. Only retained or
replacement lines are cloned from that borrowed current value. The fresh owners are
moved into the vector as it grows. Individual lines and the cursor buffer can be large
within allocation limits.

## Lines

`File::OpenRead` uses binary mode. LF and CRLF delimit lines. A bare CR, embedded NUL,
Ctrl+Z, and other bytes remain payload. Empty lines count, and a final unterminated line
is included. Counted console writes preserve line bytes and add one host newline per
selected line (CRLF on Windows, LF on Linux). Empty input produces no lines. Filenames
use the existing narrow C runtime path API.

## Errors

Success exits `0`, incorrect arguments exit `2`, and file/allocation/output errors exit
`1` with `sample-lines: ` and the original cause on stderr. The cursor is destroyed and
the file explicitly closed before output begins, so input errors produce no stdout.
Output errors can leave a prefix. Positive-count runs flush stdout before reporting
success.
