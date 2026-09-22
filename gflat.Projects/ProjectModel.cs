using Microsoft.Build.Locator;
namespace gflat.Projects;

public enum ProjectKind { Executable, SourceLibrary }

public sealed class ProjectException(string path, string message, int line = 1, int column = 1) : Exception(message)
{
    public string ProjectPath { get; } = path;
    public int Line { get; } = Math.Max(1, line);
    public int Column { get; } = Math.Max(1, column);
    public override string ToString() => $"{ProjectPath}({Line},{Column}): error GFPROJ: {Message}";
}

/// <summary>An immutable snapshot of MSBuild evaluation, without executing targets.</summary>
public sealed class ProjectModel
{
    public static readonly StringComparer Paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly Lazy<bool> registered = new(() =>
    {
        if (!MSBuildLocator.IsRegistered) MSBuildLocator.RegisterDefaults();
        return true;
    });
    public string FilePath { get; }
    public string DirectoryPath => Path.GetDirectoryName(FilePath)!;
    public string Name => Path.GetFileNameWithoutExtension(FilePath);
    public ProjectKind Kind { get; }
    public string TargetPath { get; }
    public IReadOnlyList<string> References { get; }
    public IReadOnlyList<string> Imports { get; }
    private readonly string[] sources;
    private readonly Func<string, bool> matchesGlob;

    internal ProjectModel(string path, ProjectKind kind, string targetPath, string[] sources,
        string[] references, string[] imports, Func<string, bool> matchesGlob)
    {
        FilePath = path; Kind = kind; TargetPath = targetPath; this.sources = sources;
        References = references; Imports = imports; this.matchesGlob = matchesGlob;
    }
    public static ProjectModel Load(string path) => Load(path, "Debug", "AnyCPU");
    public static ProjectModel Load(string path, string configuration, string platform)
    {
        path = Path.GetFullPath(path);
        try { _ = registered.Value; }
        catch (Exception error) { throw new ProjectException(path, "Cannot locate MSBuild. Install the .NET SDK. " + error.Message); }
        // The evaluator lives in a separate method/type: MSBuild must not be loaded
        // by the JIT until Locator has registered its assembly resolver.
        return MsBuildEvaluation.Load(path, configuration, platform);
    }
    public bool IncludesFile(string path)
    {
        path = Path.GetFullPath(path);
        return sources.Contains(path, Paths) || matchesGlob(path);
    }
    public IReadOnlyList<string> SourceFiles() => sources;
    public static bool IsGeneratedDirectory(string name) =>
        name.Equals("bin", StringComparison.OrdinalIgnoreCase) || name.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
        name == ".git" || name == ".vs" || name == ".local-notes";
}

public sealed class ProjectGraph
{
    public ProjectModel Root { get; }
    public IReadOnlyList<ProjectModel> Projects { get; }
    public IReadOnlyList<string> Sources { get; }
    private ProjectGraph(ProjectModel root, List<ProjectModel> projects, List<string> sources)
    { Root = root; Projects = projects; Sources = sources; }

    public static ProjectGraph Load(string path, Func<string, ProjectModel>? loader = null)
    {
        loader = loader ?? ProjectModel.Load;
        var active = new HashSet<string>(ProjectModel.Paths);
        var visited = new HashSet<string>(ProjectModel.Paths);
        var projects = new List<ProjectModel>();
        var sources = new List<string>();
        var sourceSet = new HashSet<string>(ProjectModel.Paths);
        ProjectModel Visit(string file)
        {
            file = Path.GetFullPath(file);
            if (!active.Add(file)) throw new ProjectException(file, "Cyclic project reference.");
            var project = loader(file);
            foreach (string reference in project.References)
            {
                if (active.Contains(reference)) throw new ProjectException(file, "Cyclic project reference: " + reference);
                if (visited.Contains(reference)) continue;
                var dependency = Visit(reference);
                if (dependency.Kind != ProjectKind.SourceLibrary)
                    throw new ProjectException(file, "Only SourceLibrary projects can be referenced: " + reference);
            }
            active.Remove(file);
            visited.Add(file);
            projects.Add(project);
            foreach (string source in project.SourceFiles()) if (sourceSet.Add(source)) sources.Add(source);
            return project;
        }
        var root = Visit(path);
        return new ProjectGraph(root, projects, sources);
    }
}
