using Standard::Memory;
using Standard::Collections;
using Standard;

namespace Pathfinding;

// Dijkstra search on an ASCII grid: '#' is blocked, '1'..'9' are entry costs,
// and every other byte costs one. Returned indices include both endpoints.
static class Grid
{
    private struct Pending
    {
        public nuint Cell;
        public nuint Cost;
        public Pending(nuint cell, nuint cost) { Cell = cell; Cost = cost; }
    }

    private static int Compare(immutable(Pending)* left, immutable(Pending)* right)
    {
        if (left.Cost < right.Cost) return -1;
        if (left.Cost > right.Cost) return 1;
        return 0;
    }

    private static void Visit(Span<readonly(char)> cells, nuint from, nuint to, nuint cost,
        Vector<nuint>* distances, Vector<nuint>* previous, PriorityQueue<Pending>* pending)
    {
        char cell = cells[to];
        if (cell == '#') return;
        nuint step = 1;
        if (cell >= '1' && cell <= '9') step = (nuint)((int)cell - (int)'0');
        nuint maximum = (nuint)0 - (nuint)1;
        if (cost >= maximum - step) throw new Exception("Path cost overflow");
        nuint next = cost + step;
        if (next >= (*distances)[to]) return;
        (*distances)[to] = next;
        (*previous)[to] = from;
        pending.Enqueue(new Pending(to, next));
    }

    public static Vector<nuint> FindPath(Span<readonly(char)> cells, nuint width, nuint start, nuint goal)
    {
        if (width == 0 || cells.Length % width != 0) throw new Exception("Grid has an invalid width");
        if (start >= cells.Length || goal >= cells.Length) throw new Exception("Grid endpoint is out of range");
        if (cells[start] == '#' || cells[goal] == '#') throw new Exception("Grid endpoint is blocked");
        Vector<nuint> distances = new(); Vector<nuint> previous = new();
        nuint maximum = (nuint)0 - (nuint)1;
        for (nuint i = 0; i < cells.Length; i++) { distances.Add(maximum); previous.Add(maximum); }
        BitSet settled = new(cells.Length);
        PriorityQueue<Pending> pending = new(Grid::Compare);
        distances[start] = 0;
        pending.Enqueue(new Pending(start, 0));
        while (!pending.IsEmpty)
        {
            Pending current = pending.Dequeue();
            if (settled[current.Cell]) continue;
            settled[current.Cell] = true;
            if (current.Cell == goal) break;
            nuint x = current.Cell % width;
            if (x > 0) Visit(cells, current.Cell, current.Cell - 1, current.Cost, &distances, &previous, &pending);
            if (x + 1 < width) Visit(cells, current.Cell, current.Cell + 1, current.Cost, &distances, &previous, &pending);
            if (current.Cell >= width) Visit(cells, current.Cell, current.Cell - width, current.Cost, &distances, &previous, &pending);
            if (current.Cell < cells.Length - width) Visit(cells, current.Cell, current.Cell + width, current.Cost, &distances, &previous, &pending);
        }
        Vector<nuint> path = new();
        if (!settled[goal]) return path;
        nuint cursor = goal;
        while (cursor != start) { path.Add(cursor); cursor = previous[cursor]; }
        path.Add(start);
        Reverse<nuint>(path.AsSpan());
        return path;
    }
}
