# Weighted grid pathfinding

Open `pathfinding.gfproj` in Visual Studio, or build with:

```powershell
gflat build examples/pathfinding/pathfinding.gfproj
```

The example uses `PriorityQueue`, `BitSet`, `Vector`, and span algorithms to
find a cheapest route through an ASCII grid, then prints it with `*` markers.
`#` blocks a cell, digits `1` through `9` give its entry cost, and other cells
cost one. Starting in a cell has no cost; the search considers four neighbors
and does not wrap between rows. No GC or platform-specific API is used.

`Grid.gf` contains the reusable search. `Grid::FindPath(cells, width, start, goal)`
borrows a counted readonly span for the duration of the call and returns an
owning `Vector<nuint>` of cell indices including both endpoints. An unreachable
goal produces an empty vector. Invalid dimensions, blocked endpoints, or
out-of-range endpoints throw. The input is never modified by the search.

The executable's `main.gf` owns its map and marks a returned path for display.
You can change that map without changing the search implementation.
