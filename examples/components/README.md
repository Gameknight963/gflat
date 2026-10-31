# Undirected connected components

Read graph edges from stdin and print the connected components of a numbered
set of nodes:

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

`components <node-count>` creates nodes `0` through `node-count - 1`. Each input
line contains two node indices separated by spaces or tabs. Blank lines are
ignored. Counts and indices are unsigned decimal integers, optionally prefixed
with `+`. Lines can contain at most 127 bytes, excluding LF or CRLF endings.

Repeated edges and self-edges are valid. Unconnected nodes remain singletons.
With zero nodes and empty input, the result is `Components: 0`.

Output begins with the component count, followed by `node representative` rows.
Nodes in the same component share a representative. The chosen representative
can change with edge order.

The program retains the node partition rather than every edge, using O(N)
storage for N nodes. See [DisjointSet](../../docs/DISJOINT_SET.md).

All edges are checked before output starts. Input errors include a line number
on stderr. Exit codes are 0 for success, 2 for invalid arguments or input, and
1 for IO or allocation failure.
