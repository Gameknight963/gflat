# Timestamp piped text lines

```console
gflat build examples/stamp-lines/stamp-lines.gfproj --configuration Release
examples/stamp-lines/bin/Release/stamp-lines.exe < input.txt > stamped.txt
```

On Linux, run examples/stamp-lines/bin/Release/stamp-lines instead.
Use matching compiler, std and platform libraries from this checkout: the example
requires the formatted LinePrefixWriter overload and optional SystemClock APIs.
It takes no arguments. Redirect or pipe text into stdin; output goes to stdout.

Each LF-delimited input line receives a prefix in the form
`[<UTC date and time>Z] `. The angle-bracket text here is a placeholder for a
real clock reading, not literal output. Date/time bytes use DateTime's canonical
form: yyyy-MM-ddTHH:mm:ss, plus a seven-digit fraction when nonzero. The Z is
added explicitly because SystemClock.GetUtcNow returns UTC by contract, while
DateTime itself carries no timezone metadata.

An empty LF-terminated line gets its own prefix. Empty input produces no output,
and a trailing LF produces no extra prefix. A bare CR is ordinary source text
for the adapter. A prefix is obtained when the first byte of a new input line
is processed, not when the original event happened. The 4096-byte counted read
loop does not buffer a whole line or allocate Strings; Console/CRT buffering
still controls when a chunk becomes available. Timestamp values can repeat or
jump as the system clock is adjusted; this is not an elapsed-time measurement.

This is a text pipe filter using the Console streams' existing host modes.
On Windows, CRT text mode can translate CRLF/LF and interpret control-Z as EOF.
The program does not change stream modes or perform its own newline normalization,
and it makes no promise of binary transport or byte-for-byte CRLF preservation.
LinePrefixWriter preserves the bytes it is given, including NUL; the host streams
can transform bytes before or after the adapter sees them.

The application formatter reads its native clock before writing '['. A clock
failure therefore emits no fragment of that new prefix, but earlier lines may
already have been written. Sink failures may leave a partial prefix or input run.
There is no rollback or retry. Success explicitly flushes stdout.

The project disables default Compile globs before the props import, includes one
clock alias file, and references one optional platform source library. Windows
selects Windows::SystemClock; the other supported host, Linux LP64, selects
Linux::SystemClock. The non-Windows project condition is intended for Linux,
not every operating system. The core writer does not choose or import a clock.
Windows requires Windows 8 / Server 2012 or newer, using the x64 support;
Linux uses the existing LP64/libc clock_gettime support.

Exit codes: 0 for successful input/output, 2 for arguments (Usage: stamp-lines),
and 1 for clock, input, or output failure (Unable to stamp input). Diagnostics
are written to stderr and explicitly flushed. No timing or timestamp value is
used as a correctness threshold.
