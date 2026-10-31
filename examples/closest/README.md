# Closest byte matches

`closest <query> <candidate> [more candidates...]` prints every candidate at the minimum
unit-cost byte edit distance from the query. It keeps ties and duplicate arguments in
their original order, making a small fuzzy-name or token shortlist. Output is
`<distance>`, a tab, and the candidate's original bytes, followed by Console's newline.
Candidates are written verbatim without escaping.

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
`examples/closest/bin/Release/closest.exe` directly (prefix it with `./` on Linux). The
`.exe` suffix is the current project output convention on both platforms. At least one
candidate is required. Empty query and candidate arguments are valid. Arguments
beginning with `-` are ordinary text.

## Matching

Distances count single-byte insertion, deletion, and substitution. Transposition costs
two edits. Comparison is case-sensitive and byte-based. Arguments use the platform's
narrow-character encoding and end at NUL. The underlying [EditDistance
API](../../docs/EDIT_DISTANCE.md) also accepts counted binary strings.

## Storage and work

A reusable scratch row grows to `min(query length, candidate length) + 1` elements. A
second vector stores winning argument indices. Each nonempty pair takes O(query length *
candidate length) work.

## Errors

Ranking finishes before output starts. Exit codes are 0 for success, 2 for usage errors,
and 1 for allocation, computation, or output failure. Failures write `closest: Unable to
compare candidates or write output` to stderr. An output failure can leave a printed
prefix.
