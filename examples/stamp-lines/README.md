# Timestamp piped text lines

```console
gflat build examples/stamp-lines/stamp-lines.gfproj --configuration Release
examples/stamp-lines/bin/Release/stamp-lines.exe < input.txt > stamped.txt
```

On Linux, prefix the executable path with `./`. The default project output uses `.exe`.
Pipe or redirect text into stdin. Output goes to stdout.

## Prefixes

Each LF-delimited input line receives a prefix in the form `[<UTC date and time>Z] `.
The angle-bracket text here is a placeholder for a clock reading. Date/time bytes use
DateTime's canonical form: yyyy-MM-ddTHH:mm:ss, plus a seven-digit fraction when
nonzero. The Z is added explicitly because SystemClock.GetUtcNow returns UTC by
contract, while DateTime itself carries no timezone metadata.

An empty LF-terminated line gets its own prefix. Empty input produces no output, and a
trailing LF produces no extra prefix. A bare CR is ordinary source text for the adapter.
A prefix is obtained when the first byte of a new input line is processed. The 4096-byte
read loop forwards chunks as they become available through Console/CRT buffering.
Timestamps can repeat or jump when the system clock is adjusted.

## Console behavior

This is a text pipe filter using the Console streams' existing host modes. On Windows,
CRT text mode can translate CRLF/LF and interpret control-Z as EOF. LinePrefixWriter
preserves the bytes it receives, including NUL. Host streams can transform bytes before
or after the adapter sees them.

## Errors

The application formatter reads its native clock before writing '['. A clock failure
therefore emits no fragment of that new prefix, but earlier lines may already have been
written. Sink failures may leave a partial prefix or input run. Success flushes stdout.

Exit codes: 0 for successful input/output, 2 for arguments (Usage: stamp-lines), and 1
for clock, input, or output failure (Unable to stamp input). Diagnostics are written to
stderr and flushed.

## Clock selection

The project disables default Compile globs before the props import, includes one clock
alias file, and references one optional platform source library. Windows selects
Windows::SystemClock. Linux LP64 selects Linux::SystemClock. Windows requires Windows 8
/ Server 2012 or newer, using the x64 support. Linux uses the existing LP64/libc
clock_gettime support.
