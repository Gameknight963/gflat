# Closest byte matches

`closest <query> <candidate> [more candidates...]` prints every candidate at the
minimum unit-cost byte edit distance from the query. It keeps ties and duplicate
arguments in their original order, making a small fuzzy-name or token shortlist.
Output is `<distance>`, a tab, and the candidate's original bytes, followed by
Console's newline. Candidates are written verbatim without escaping.

```text
gflat build examples/closest/closest.gfproj --configuration Release --run -- book back boon books boon
```

This prints (`\t` below denotes a tab byte):

```text
1	boon
1	books
1	boon
```

You can also build without `--run`, then invoke
`examples/closest/bin/Release/closest.exe` directly (prefix it with `./` on Linux).
The `.exe` suffix is the current project output convention on both platforms.
At least one candidate is required; empty query and candidate arguments are valid.
There are no options, so arguments beginning with `-` are ordinary text.

Distances count single-byte insertion, deletion, and substitution; transposition
costs two edits. Comparison is case-sensitive, with no Unicode decoding,
normalization, or case folding. It measures the argument bytes supplied to the
program. Paths and arguments use the platform's narrow-character encoding; this
example does not add a Unicode command-line ABI on Windows. Command-line strings
end at NUL; arbitrary counted binary strings belong in the underlying EditDistance
API rather than this CLI.

The program borrows argv for the duration of main. It creates no owning String
per candidate and introduces no GC. One `Vector<nuint>` row is reused and grows
only when a larger `min(query length, candidate length) + 1` workspace is needed;
empty inputs need no row. Initialized slots are reused for later candidates.
Another vector holds only winning argument indices. Their backing capacities
follow normal geometric Vector growth, and ordinary RAII releases both on exit.
Distance computation for each nonempty pair uses O(query length * candidate
length) work. Finding argument lengths and writing results add linear work in
the bytes scanned or written, including when the query is empty.

All ranking finishes before stdout starts. Success is exit 0 after a final flush;
usage is exit 2, and allocation, computation, or output failure is exit 1 with the
static stderr diagnostic `closest: Unable to compare candidates or write output`.
Output failure may leave a prefix already written. No files are opened and no
candidate is copied merely to retain its spelling.
