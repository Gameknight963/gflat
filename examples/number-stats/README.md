# Streaming number statistics

Read one signed 64-bit decimal integer per input line and print the count, exact
signed 128-bit sum, and (when nonempty) minimum and maximum. Blank lines are
ignored. A final line without a newline is accepted.

```powershell
gflat build examples/number-stats/number-stats.gfproj -o artifacts/number-stats.exe
"9223372036854775807", "9223372036854775807" | ./artifacts/number-stats.exe
```

```text
Count: 2
Sum: 18446744073709551614
Min: 9223372036854775807
Max: 9223372036854775807
```

The program streams through a reusable `char[128]`. Each line may contain at most
**127 payload bytes**, including surrounding whitespace, because capacity also
includes the terminating zero. The line reader removes LF/CRLF before enforcing
this payload limit. The entire line is read before parsing: 127 leading zeros
are accepted, but 128 leading zeros are rejected even though their numeric value
would fit. Oversized blank lines are rejected too.

`Span<readonly(char)>.Trim()` borrows the buffer and removes only ASCII space,
tab, LF, vertical tab, form feed, and CR. Counted `TryParseLong` then requires a
complete signed decimal integer in `long` range; embedded zeros, non-ASCII
whitespace, suffixes, and numeric overflow are errors. An optional `+` or `-`
is accepted. The guarded pointer-sized line counter bounds the number of values,
so their sum fits `extralong` on supported 64-bit targets.

Normal processing creates no owning String, Vector, or managed object. Numeric
formatting writes stack buffers directly through `Console::Out`; the host stdio
and exception runtime still have their normal buffering and allocation behavior.
Console input keeps its host mode, including Windows text-mode translation.

Exit codes are `0` success, `2` malformed or oversized input, and `1` another
exception (for example, an input/output failure). Input errors report a one-based
line number to stderr and produce no statistics. Successful empty input prints
only `Count: 0` and `Sum: 0`. Output errors can leave partial output; stdout is
flushed before reporting success.
