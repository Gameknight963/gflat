# Integer sequence tool

A small command-line tool using parsing extensions, lazy integer queries,
property-based `foreach`, and direct formatting into a console writer.

```text
gflat build examples/seq/seq.gfproj --run -- -3 7 --even
```

This prints `-2`, `0`, and `2`, each on its own line. Omit `--even` to print
every integer. Count must be nonnegative and the last integer must fit `int`.
Zero count produces no output. Exit codes: zero for success, one for invalid
numbers/ranges or output failure, two for usage errors.

The query owns no heap storage. Each number is formatted from a stack buffer
directly into stdout, without constructing a `String`. The normal path does not
call `Memory::Allocate`; libc may still buffer stdout internally. Text output
uses the platform's newline handling. Error messages currently use stdout too.

Build without `--run` to invoke `examples/seq/bin/Debug/seq.exe` directly
(prefix with `./` on Linux). The `.exe` suffix is currently used on both platforms.
