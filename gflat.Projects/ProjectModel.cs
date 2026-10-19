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
    public string FilePath { get; }
    public string DirectoryPath => Path.GetDirectoryName(FilePath)!;
    public string Name => Path.GetFileNameWithoutExtension(FilePath);
    public ProjectKind Kind { get; }
    public string Configuration { get; }
    public string Platform { get; }
    /// <summary>Native executable path; source libraries have no output artifact.</summary>
    public string? TargetPath { get; }
    public int ExceptionReserveBytes { get; }
    public int OptimizationLevel { get; }
    public bool NoStdLib { get; }
    public bool NoBuiltins { get; }
    public string? SdkRoot { get; }
    public IReadOnlyList<string> References { get; }
    public IReadOnlyList<string> Imports { get; }
    private readonly string[] sources;
    private readonly Func<string, bool> matchesGlob;

    internal ProjectModel(string path, ProjectKind kind, string? targetPath, string[] sources,
        string[] references, string[] imports, Func<string, bool> matchesGlob, int exceptionReserveBytes, int optimizationLevel, string? sdkRoot = null, bool noStdLib = false, string configuration = "Debug", string platform = "AnyCPU", bool noBuiltins = false)
    {
        Configuration = configuration;
        Platform = platform;
        NoStdLib = noStdLib;
        NoBuiltins = noBuiltins;
        ExceptionReserveBytes = exceptionReserveBytes;
        OptimizationLevel = optimizationLevel;
        SdkRoot = sdkRoot;
        FilePath = path; Kind = kind; TargetPath = targetPath; this.sources = sources;
        References = references; Imports = imports; this.matchesGlob = matchesGlob;
    }
    public static ProjectModel Load(string path) => Load(path, null, null);
    public static ProjectModel Load(string path, string? configuration, string? platform, int? exceptionReserveBytes = null, int? optimizationLevel = null, bool? noStdLib = null, bool asReference = false, bool? debugSymbols = null, bool? noBuiltins = null)
    {
        path = Path.GetFullPath(path);
        try { MsBuildHost.Initialize(); }
        catch (IOException error) { throw new ProjectException(path, error.Message); }
        return MsBuildEvaluation.Load(path, configuration, platform, exceptionReserveBytes, optimizationLevel, noStdLib, asReference, debugSymbols, noBuiltins);
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

    /// <summary>Evaluates the root normally and each dependency in reference context.</summary>
    /// <param name="loader">Optional evaluator receiving the project path and whether it is a reference.</param>
    public static ProjectGraph Load(string path, Func<string, bool, ProjectModel>? loader = null)
    {
        if (loader == null)
        {
            ProjectModel? rootSettings = null;
            loader = (file, asReference) => rootSettings == null
                ? rootSettings = ProjectModel.Load(file)
                : ProjectModel.Load(file, rootSettings.Configuration, rootSettings.Platform, asReference: asReference);
        }
        var active = new HashSet<string>(ProjectModel.Paths);
        var visited = new HashSet<string>(ProjectModel.Paths);
        var projects = new List<ProjectModel>();
        var sources = new List<string>();
        var sourceSet = new HashSet<string>(ProjectModel.Paths);
        ProjectModel Visit(string file, bool asReference)
        {
            file = Path.GetFullPath(file);
            if (!active.Add(file)) throw new ProjectException(file, "Cyclic project reference.");
            var project = loader(file, asReference);
            foreach (string reference in project.References)
            {
                if (active.Contains(reference)) throw new ProjectException(file, "Cyclic project reference: " + reference);
                if (visited.Contains(reference)) continue;
                var dependency = Visit(reference, asReference: true);
                if (dependency.Kind != ProjectKind.SourceLibrary)
                    throw new ProjectException(file, "Only projects with OutputType=SourceLibrary can be referenced: " + reference);
            }
            active.Remove(file);
            visited.Add(file);
            projects.Add(project);
            foreach (string source in project.SourceFiles()) if (sourceSet.Add(source)) sources.Add(source);
            return project;
        }
        var root = Visit(path, asReference: false);
        return new ProjectGraph(root, projects, sources);
    }
}
