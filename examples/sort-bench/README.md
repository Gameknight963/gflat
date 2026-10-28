# Timed integer sorting

```console
gflat build examples/sort-bench/sort-bench.gfproj --configuration Release
examples/sort-bench/bin/Release/sort-bench.exe 100000
```

On Linux, prefix the executable path with `./`; the default project output
still uses `.exe`.
Use a toolchain containing Stopwatch and the optional platform source projects.
For Visual Studio, these first ship in VSIX 0.14.158.

Syntax: `sort-bench [count]`. The default is 100000 integers; the accepted
range is 0 through 1000000. A fixed PseudoRandom seed of 17 generates one
signed 32-bit value per element, stored in both inputs. There is no seed option.

The application explicitly selects its native clock through one global alias
file. The project includes Clock.Windows.gf and the Windows platform source
library on Windows; otherwise it selects Clock.Linux.gf and the Linux source
library. These are the two supported hosts, not a generic fallback for every
non-Windows platform. The project disables default compile globs and lists the
selected alias explicitly, so the two aliases are not included together. Neither the core
Stopwatch nor the platform libraries select a global default provider.

Only Sort and StableSort calls are timed. StableSort's own scratch allocations
are part of its measurement; creating and filling the equal inputs, checking
results, and reporting are outside both intervals. After both sorts, the tool
checks ascending order, identical results, and conservation of the input sum,
then prints Count, a signed
64-bit Checksum, and Sort/StableSort durations formatted as TimeSpan values.
The checksum is deterministic for a given count. Durations are not.
Reports write through Console::Out without constructing per-report Strings.

This is a small Stopwatch and conditional-project-reference example, not a
benchmark suite. It takes one measurement of each operation in a fixed order.
Scheduling, clock resolution, cache state, allocation, and system load affect
results. No speed claim or timing threshold follows from a single run, and
equal integer results do not demonstrate stability for records with equal keys.

Exit codes: 0 for verified results and successful output; 2 for invalid usage;
1 for allocation, clock, or output failure; 3 for internal verification failure.
Diagnostics go to stderr. Output is explicitly flushed before success.
