# Time integer sorting

Compare `Sort` and `StableSort` on the same generated integers:

```console
gflat build examples/sort-bench/sort-bench.gfproj --configuration Release
examples/sort-bench/bin/Release/sort-bench.exe 100000
```

`sort-bench [count]` defaults to 100000 values and accepts counts from zero
through 1000000. A fixed seed makes the input and printed checksum repeatable.

Each sort is timed once. The measurement includes its own scratch allocation.
Generating inputs and checking results happen outside the timed intervals.
Scheduling, cache state, clock resolution, and system load affect the result.

The project selects a Windows or Linux clock through a platform-specific alias
file. See [Stopwatch](../../docs/STOPWATCH.md) for clock providers.

Exit codes are 0 for verified results, 2 for invalid arguments, 1 for clock,
allocation, or output failure, and 3 if result verification fails. Errors go
to stderr.
