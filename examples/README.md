# Runnable examples

The projects in [examples.slnx](examples.slnx) cover language features and small
command-line tools. Follow each linked page for inputs, options, and limitations.

From the repository or extracted bundle root, with `gflat` on your PATH:

```console
gflat build examples/hello/hello.gfproj --run
gflat build examples/hexdump/hexdump.gfproj --run -- input.bin
```

Arguments after `--` go to the program. For build configuration and toolchain
setup, see [projects](../docs/PROJECTS.md).

In Visual Studio, open [examples.slnx](examples.slnx), set an executable project
as the startup project, then use **F5** or **Ctrl+F5**. The **gflat application**
launch page sets arguments and working directory; see [debugging projects](../docs/PROJECTS.md#running-in-visual-studio).

## Language and ownership

- [hello](hello/main.gf): a minimal native executable.
- [interpolation](interpolation/main.gf): interpolate strings and a custom formattable value.
- [values](values/main.gf): move owning strings into and out of a vector.
- [queries](queries/main.gf): filter, map, enumerate, and materialize a borrowed vector query.
- [providers](providers/README.md): select static interface providers at compile time.
- [managed](managed/README.md): keep managed references in native vector storage.

## Files and lines

- [search](search/README.md): print file lines containing a literal byte substring.
- [tail](tail/README.md): print the last lines of a file.
- [sample-lines](sample-lines/README.md): sample file lines without replacement.
- [sort-lines](sort-lines/README.md): sort stdin lines in ordinal byte order.
- [stamp-lines](stamp-lines/README.md): prefix piped lines with UTC timestamps.
- [word-count](word-count/README.md): count case-insensitive ASCII words in a file.
- [number-stats](number-stats/README.md): stream integer counts, exact sums, and extrema.

## Structured text and binary files

- [csv-column](csv-column/README.md): extract one CSV column.
- [csv-sum](csv-sum/README.md): sum an integer CSV column.
- [csv-to-json](csv-to-json/README.md): convert CSV records into JSON string arrays.
- [json-check](json-check/README.md): validate JSON and report token count and nesting.
- [json-get](json-get/README.md): select a JSON value with a plain JSON Pointer.
- [json-minify](json-minify/README.md): remove JSON whitespace while preserving token spelling.
- [json-pretty](json-pretty/README.md): indent JSON while preserving token spelling.
- [settings-json](settings-json/README.md): convert simple key=value settings into a JSON object.
- [quote-lines](quote-lines/README.md): encode UTF-8 text lines as JSON strings.
- [unquote-lines](unquote-lines/README.md): decode lines containing JSON string literals.
- [transcode](transcode/README.md): convert files between UTF-8 and UTF-16LE.
- [checksum](checksum/README.md): compute file checksums or SHA-256/SHA-512 digests.
- [hexdump](hexdump/README.md): print hexadecimal byte rows with an optional initial skip.
- [riff-list](riff-list/README.md): list top-level RIFF chunk offsets and lengths.
- [png-list](png-list/README.md): list PNG chunk offsets and lengths, checking framing and CRCs.

## Numbers, algorithms, and commands

- [big-calc](big-calc/README.md): perform exact arbitrary-precision integer arithmetic.
- [ratio-calc](ratio-calc/README.md): calculate with exact fractions.
- [time-convert](time-convert/README.md): convert explicit-offset timestamps and Unix time.
- [seq](seq/README.md): generate integer sequences through lazy queries.
- [sort-bench](sort-bench/README.md): time integer sorting.
- [closest](closest/README.md): find command-line candidates at minimum byte edit distance.
- [components](components/README.md): group numbered graph nodes into connected components.
- [pathfinding](pathfinding/README.md): find a cheapest route through a weighted grid.
- [ip-count](ip-count/README.md): count equivalent IPv4 and IPv6 address spellings.
- [tasks](tasks/README.md): manage an interactive task list in memory.
