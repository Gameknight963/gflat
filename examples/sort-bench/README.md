# Timed integer sorting

```console
gflat build examples/sort-bench/sort-bench.gfproj --configuration Release
examples/sort-bench/bin/Release/sort-bench.exe 100000
```

On Linux, prefix the executable path with `./`. The default project output uses `.exe`.

## Input

Syntax: `sort-bench [count]`. The default is 100000 integers. The accepted range is 0
through 1000000. A fixed PseudoRandom seed of 17 generates one signed 32-bit value per
element, stored in both inputs.

## Clock selection

The application explicitly selects its native clock through one global alias file. The
project includes Clock.Windows.gf and the Windows platform source library on Windows.
Linux uses Clock.Linux.gf and the Linux source library. The project lists the selected
alias explicitly.

## Measurement

Only Sort and StableSort calls are timed. StableSort's own scratch allocations are part
of its measurement. Creating and filling the equal inputs, checking results, and
reporting are outside both intervals. After both sorts, the tool checks ascending order,
identical results, and conservation of the input sum, then prints Count, a signed 64-bit
Checksum, and Sort/StableSort durations formatted as TimeSpan values. The checksum is
deterministic for a given count. Durations are not. Reports write through Console::Out
without constructing per-report Strings.

The tool takes one measurement of each operation in a fixed order. Scheduling, clock
resolution, cache state, allocation, and system load affect results.

## Errors

Exit codes:

- 0 for verified results and successful output
- 1 for allocation, clock, or output failure
- 2 for invalid usage
- 3 for internal verification failure
Diagnostics go to stderr. Output is explicitly flushed before success.
