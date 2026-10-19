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
            using std;
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
    public void InvalidDimensionsAndEndpointsHaveUsefulErrors()
    {
        var result = CompilerTestHelper.Run(Sources("""
            using std;
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
        string[] files = ["Collections/IEnumerable.gf", "Collections/Enumerable.gf", "Collections/Algorithms.gf",
            "Vector.gf", "Span.gf", "BitSet.gf", "PriorityQueue.gf"];
        string grid = Path.Combine(TestSupport.ProjectFileFixture.RepositoryRoot, "examples", "pathfinding", "Grid.gf");
        return files.Select(file => new SourceFile(file, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Std/core", file))))
            .Append(new SourceFile(grid, File.ReadAllText(grid))).Append(new SourceFile("main.gf", program));
    }
}
