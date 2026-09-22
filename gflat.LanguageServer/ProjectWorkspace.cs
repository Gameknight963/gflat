using gflat.Projects;

namespace gflat.LanguageServer;

/// <summary>Project evaluation is cached between edits; file-system changes invalidate it.</summary>
public sealed class ProjectWorkspace
{
    private readonly Dictionary<string, ProjectGraph> previous = new(Workspace.Paths);
    private readonly Dictionary<string, (ProjectGraph? Graph, ProjectException? Error, string[] Paths)> cache = new(Workspace.Paths);

    public void Invalidate() => cache.Clear();

    public (ProjectGraph? Graph, ProjectException? Error, string[] Paths) Load(string file)
    {
        if (cache.TryGetValue(file, out var found)) return found;
        var attempted = new HashSet<string>(Workspace.Paths) { file };
        try
        {
            var graph = ProjectGraph.Load(file, path => { attempted.Add(path); return ProjectModel.Load(path); });
            foreach (var project in graph.Projects) attempted.UnionWith(project.Imports);
            previous[file] = graph;
            return cache[file] = (graph, null, attempted.ToArray());
        }
        catch (ProjectException error)
        {
            var graph = previous.GetValueOrDefault(file);
            attempted.Add(error.ProjectPath);
            if (graph != null)
                foreach (var project in graph.Projects)
                {
                    attempted.Add(project.FilePath);
                    attempted.UnionWith(project.Imports);
                }
            return cache[file] = (graph, error, attempted.ToArray());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return cache[file] = (previous.GetValueOrDefault(file), new ProjectException(file, error.Message), attempted.ToArray());
        }
    }

    public static string? FindProject(string source)
    {
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(source))!); directory != null; directory = directory.Parent)
        {
            if (!directory.Exists) continue;
            var projects = directory.GetFiles("*.gfproj").OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
            if (projects.Length > 1) throw new ProjectException(projects[0].FullName, "Only one .gfproj is supported per directory.");
            if (projects.Length == 1) return projects[0].FullName;
        }
        return null;
    }
}
