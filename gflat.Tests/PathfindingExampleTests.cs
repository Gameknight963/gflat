namespace gflat.Tests;

public sealed class PathfindingExampleTests
{
    [Fact]
    public void ActualProjectFindsACheapRouteAndPrintsIt()
    {
        string project = Path.Combine(TestSupport.ProjectFileFixture.RepositoryRoot,
            "examples", "pathfinding", "pathfinding.gfproj");
        var graph = Projects.ProjectGraph.Load(project);
        var result = CompilerTestHelper.Run(graph.Sources.Select(path => new SourceFile(path, File.ReadAllText(path))));
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Route: 23 cells, cost 22", result.StandardOutput);
        Assert.Contains("S****#******", result.StandardOutput);
        Assert.Contains(".....#....G*", result.StandardOutput);
    }

    [Fact]
    public void WeightedSearchUsesCheaperDetourAndHandlesTrivialAndUnreachableGoals()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Collections;
            using Pathfinding;
            int main() {
                Span<readonly(char)> cells = new("S9G...", 6);
                Vector<nuint> path = Grid::FindPath(cells, 3, 0, 2);
                if (path.Count != 5 || path[0] != 0 || path[1] != 3 || path[2] != 4 || path[3] != 5 || path[4] != 2) return 1;
                Span<readonly(char)> blocked = new("S#G###", 6);
                Vector<nuint> unreachable = Grid::FindPath(blocked, 3, 0, 2);
                if (unreachable.Count != 0) return 2;
                Span<readonly(char)> single = new("x", 1);
                Vector<nuint> same = Grid::FindPath(single, 1, 0, 0);
                if (same.Count != 1 || same[0] != 0) return 3;
                if (cells[1] != '9') return 4;
                return 0;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void RandomWeightedGridsMatchIndependentBellmanFordCosts()
    {
        var program = new System.Text.StringBuilder("""
            using std; using std::Collections;
            using Pathfinding;
            bool Verify(immutable(char*) text, int expected) {
                Span<readonly(char)> cells = new(text, 42);
                Vector<nuint> path = Grid::FindPath(cells, 7, 0, 41);
                if (expected < 0) return path.Count == 0;
                if (path.Count == 0 || path[0] != 0 || path[path.Count - 1] != 41) return false;
                int cost = 0;
                for (nuint i = 1; i < path.Count; i++) {
                    nuint previous = path[i - 1]; nuint current = path[i];
                    if (current >= 42 || cells[current] == '#') return false;
                    nuint difference = 0;
                    if (current > previous) difference = current - previous;
                    else difference = previous - current;
                    if (difference != 7 && (difference != 1 || current / 7 != previous / 7)) return false;
                    if (cells[current] >= '1' && cells[current] <= '9') cost += (int)cells[current] - (int)'0';
                    else cost++;
                }
                return cost == expected;
            }
            int main() {
            """);
        var random = new Random(17897);
        for (int trial = 0; trial < 40; trial++)
        {
            var cells = new char[42];
            for (int i = 0; i < cells.Length; i++)
            {
                int choice = random.Next(100);
                cells[i] = choice < 25 ? '#' : choice < 60 ? (char)('0' + random.Next(1, 10)) : '.';
            }
            cells[0] = 'S'; cells[^1] = 'G';
            int expected = ReferenceCost(cells, 7);
            program.AppendLine($"if (!Verify(\"{new string(cells)}\", {expected})) return {trial + 100};");
        }
        program.AppendLine("return 0; }");
        Assert.Equal(0, CompilerTestHelper.Run(Sources(program.ToString())).ExitCode);
    }

    private static int ReferenceCost(char[] cells, int width)
    {
        const int unreachable = 1_000_000;
        int[] costs = Enumerable.Repeat(unreachable, cells.Length).ToArray();
        costs[0] = 0;
        for (int pass = 0; pass < cells.Length; pass++)
        {
            bool changed = false;
            for (int cell = 0; cell < cells.Length; cell++)
            {
                if (costs[cell] == unreachable || cells[cell] == '#') continue;
                if (cell % width != 0) Relax(cell, cell - 1);
                if (cell % width != width - 1) Relax(cell, cell + 1);
                if (cell >= width) Relax(cell, cell - width);
                if (cell < cells.Length - width) Relax(cell, cell + width);
            }
            if (!changed) break;

            void Relax(int from, int to)
            {
                if (cells[to] == '#') return;
                int step = cells[to] is >= '1' and <= '9' ? cells[to] - '0' : 1;
                int next = costs[from] + step;
                if (next < costs[to]) { costs[to] = next; changed = true; }
            }
        }
        return costs[^1] == unreachable ? -1 : costs[^1];
    }

    [Fact]
    public void InvalidDimensionsAndEndpointsHaveUsefulErrors()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std; using std::Collections;
            using Pathfinding;
            int main() {
                Span<readonly(char)> cells = new("S#G...", 6); int caught = 0;
                try { Grid::FindPath(cells, 0, 0, 2); } catch (ref Exception error) { caught++; }
                try { Grid::FindPath(cells, 4, 0, 2); } catch (ref Exception error) { caught++; }
                try { Grid::FindPath(cells, 3, 6, 2); } catch (ref Exception error) { caught++; }
                try { Grid::FindPath(cells, 3, 0, 6); } catch (ref Exception error) { caught++; }
                try { Grid::FindPath(cells, 3, 1, 2); } catch (ref Exception error) { caught++; }
                try { Grid::FindPath(cells, 3, 0, 1); } catch (ref Exception error) { caught++; }
                Span<readonly(char)> empty = new();
                try { Grid::FindPath(empty, 1, 0, 0); } catch (ref Exception error) { caught++; }
                return caught - 7;
            }
            """));
        Assert.Equal(0, result.ExitCode);
    }

    private static IEnumerable<SourceFile> Sources(string program)
    {
        string[] files = ["Collections/IEnumerable.gf", "Queries/Enumerable.gf", "Collections/Algorithms.gf",
            "Collections/Vector.gf", "Span.gf", "Collections/BitSet.gf", "IEquatable.gf", "IHashable.gf", "Hashing.gf", "Collections/PriorityQueue.gf"];
        string grid = Path.Combine(TestSupport.ProjectFileFixture.RepositoryRoot, "examples", "pathfinding", "Grid.gf");
        return files.Select(file => new SourceFile(file, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", file))))
            .Append(new SourceFile(grid, File.ReadAllText(grid))).Append(new SourceFile("main.gf", program));
    }
}
