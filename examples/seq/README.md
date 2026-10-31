# Integer sequences

Print an integer range, optionally selecting its even values:

```text
gflat build examples/seq/seq.gfproj --run -- -3 7 --even
```

This prints `-2`, `0`, and `2`, each on its own line. The second argument is a
count, so the full range here is `-3` through `3`. Omit `--even` to print every
value. Count must be nonnegative, and the last value must fit `int`.

The example uses [lazy integer queries](../../Standard/core/Queries/README.md)
and formats values directly to stdout.

Exit codes are 0 for success, 1 for invalid ranges or output failure, and 2 for
invalid arguments. Error messages are also written to stdout.
