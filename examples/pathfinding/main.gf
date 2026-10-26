using std::Memory;
using std::Collections;
using std;
using Pathfinding;

int main()
{
    nuint width = 12;
    String map = s"S....#......";
    map.Append(".###.#.####.");
    map.Append("...#...#....");
    map.Append(".#.#####.##.");
    map.Append(".#...999....");
    map.Append(".###.###.##.");
    map.Append(".....#....G.");
    Span<readonly(char)> cells = new(map.Data, map.Length);
    Vector<nuint> route = Grid::FindPath(cells, width, 0, width * 6 + 10);
    if (route.Count == 0) { Console::WriteLine("No route found."); return 1; }

    nuint cost = 0;
    for (nuint i = 1; i < route.Count; i++)
    {
        char cell = map[route[i]];
        if (cell >= '1' && cell <= '9') cost += (nuint)((int)cell - (int)'0');
        else cost++;
        if (cell != 'G') map[route[i]] = '*';
    }
    for (nuint row = 0; row < map.Length / width; row++)
        Console::WriteLine(map.Data + row * width, width);
    String summary = s$"Route: {route.Count} cells, cost {cost}";
    Console::WriteLine(ref summary);
    return 0;
}
