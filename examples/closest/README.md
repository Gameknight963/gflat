# Closest byte matches

`closest <query> <candidate> [more candidates...]` finds every candidate at the
minimum edit distance from the query. Ties and duplicate arguments keep their
original order.

```text
gflat build examples/closest/closest.gfproj --configuration Release --run -- book back boon books boon
```

The result contains a distance, a tab, and the candidate:

```text
1	boon
1	books
1	boon
```

Insertion, deletion, and substitution each cost one edit. Transposition costs
two. Matching is case-sensitive and compares bytes in the platform's native
command-line encoding. Empty arguments are valid, and arguments beginning with
`-` are treated as text.

The [EditDistance API](../../docs/EDIT_DISTANCE.md) supplies the comparison and
reuses scratch storage between candidates.

Exit codes are 0 for success, 2 for invalid arguments, and 1 for computation,
allocation, or output failure. Errors go to stderr.
