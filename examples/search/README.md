# Search file lines

Print file lines containing a literal byte substring:

```text
gflat build examples/search/search.gfproj --run -- Standard/libc/IO/File.gf IOException
```

Matching is case-sensitive by default. An empty search string matches every
line. LF and CRLF delimit lines, a bare CR remains data, and a final
unterminated line is included. Output uses platform newlines.

Pass `--ignore-case` before the filename to match ASCII letters regardless of
case:

```text
gflat build examples/search/search.gfproj --run -- --ignore-case input.txt needle
```

Other bytes remain distinct. Paths and search arguments use the platform's
narrow-character encoding. The line buffer grows to hold the longest line.

Exit codes are 0 for a completed search, 1 for IO failure, and 2 for invalid
arguments. Matching lines go to stdout and errors to stderr. An error can
leave earlier matches already written.
