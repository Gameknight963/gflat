# Undirected connected components

Build a partition of a fixed set of numbered nodes from stdin edge pairs, then print the
component count and a representative for each node:

```powershell
gflat build examples/components/components.gfproj -o artifacts/components.exe
"0 1", "1 2", "3 4" | ./artifacts/components.exe 6
```

```text
Components: 3
0 0
1 0
2 0
3 3
4 3
5 5
```

## Input

Syntax: `components <node-count>`. Nodes are the fixed universe `0..node-count-1`. Each
nonempty input line contains exactly two unsigned decimal indices, separated by ASCII
spaces and/or tabs. Space/tab-only lines are ignored. The unsigned parser permits an
optional leading `+`, rejects minus signs, embedded NUL, nondecimal bytes, trailing
data, and numeric overflow. The command line count follows the same unsigned syntax. A
third token, a missing token, and indices outside the declared universe are input
errors.

An edge joins its two endpoints without direction. Self-edges and repeated edges are
valid and do not add components. Zero nodes with empty or space/tab-only input prints
`Components: 0` and no node rows. Empty input with a nonzero count leaves every node as
a singleton.

Each line may contain at most 127 payload bytes in a reusable `char[128]` buffer,
including all spaces and tabs. The reader removes LF/CRLF before enforcing the limit and
accepts a final line without a newline. Oversized blank lines are errors too. Bare CR,
vertical tab, and form feed are not edge separators. Console stdin retains the host's
text-mode behavior, including Windows text translation.

## Output

Output starts with `Components: N`, followed by ascending `index representative` rows.
Representatives are ordinary DisjointSet roots: they identify connected components and
may change with edge order. Compare component membership rather than relying on a
particular root label.

## Errors

No stdout is written until every input line has validated. An invalid late edge
therefore produces no partial component listing. Once output begins, an output failure
may leave a partial listing. Stdout is flushed before success. Diagnostics go to stderr,
are flushed, and input errors include a one-based line number. Exit codes are 0 success,
2 invalid arguments/input or an oversized line, and 1 an input/output or allocation
failure.

## Storage and work

DisjointSet owns two `nuint` arrays for the node universe. Input uses a fixed buffer.
The program uses O(N) storage and O(M log N + N log N) work for M edges and the final
N-node listing, plus input parsing. Allocation failure returns 1.
