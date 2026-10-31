# Weighted grid pathfinding

Find a cheapest route through an ASCII grid and print it with `*` markers:

```powershell
gflat build examples/pathfinding/pathfinding.gfproj
```

`#` blocks a cell. Digits `1` through `9` give its entry cost, and other cells
cost one. The search considers four orthogonal neighbors. Starting in a cell
has no cost.

Change the map in `main.gf` to try another grid. `Grid.gf` contains the reusable
`Grid::FindPath(cells, width, start, goal)` function. It returns an owning
`Vector<nuint>` containing the route, including both endpoints. An unreachable
goal produces an empty vector. Invalid dimensions or endpoints throw.
