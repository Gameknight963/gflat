# Number statistics

Read signed 64-bit integers from stdin and print the count, exact 128-bit sum,
minimum, and maximum:

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

Each nonblank line contains one decimal integer with an optional sign.
Surrounding ASCII whitespace is ignored. LF and CRLF end lines, and a final
unterminated line is accepted. A line may contain at most 127 bytes, including
whitespace and leading zeroes.

Input errors include a line number on stderr. Statistics are printed after all
input has passed validation. Empty input prints `Count: 0` and `Sum: 0`.

Exit codes are 0 for success, 2 for invalid or oversized input, and 1 for IO or
allocation failure.
