# Timestamp piped text

Prefix each input line with its processing time in UTC:

```console
gflat build examples/stamp-lines/stamp-lines.gfproj --configuration Release
examples/stamp-lines/bin/Release/stamp-lines.exe < input.txt > stamped.txt
```

Prefixes have the form `[yyyy-MM-ddTHH:mm:ssZ] `, with seven fractional digits
when nonzero. A timestamp is read when the first byte of a new line arrives.
Timestamps can repeat or jump when the system clock is adjusted.

LF starts a new line, and empty lines receive prefixes. Input is forwarded in
chunks. Host console modes still apply, including CRLF translation and Ctrl+Z
handling on Windows.

The project selects Windows or Linux `SystemClock` through a platform alias.
It requires Windows 8 / Server 2012 or newer, or Linux LP64.

The command takes no arguments. Exit codes are 0 for success, 2 for arguments,
and 1 for clock or IO failure. Errors go to stderr. A failure can leave partial
output.
