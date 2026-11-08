namespace gflat.Projects;

public enum ProjectKind { Executable, SourceLibrary, StaticLibrary, DynamicLibrary }

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
    public NativeLinkInputs NativeLinkInputs { get; }
    public PreprocessorInputs PreprocessorInputs { get; }
    public string? RuntimeLibrary { get; }
    public bool IsNative { get; }
    private readonly string[] sources;
    private readonly Func<string, bool> matchesGlob;

    internal ProjectModel(string path, ProjectKind kind, string? targetPath, string[] sources,
        string[] references, string[] imports, Func<string, bool> matchesGlob, int exceptionReserveBytes, int optimizationLevel, string? sdkRoot = null, bool noStdLib = false, string configuration = "Debug", string platform = "AnyCPU", bool noBuiltins = false, NativeLinkInputs? nativeLinkInputs = null, PreprocessorInputs? preprocessorInputs = null, string? runtimeLibrary = null, bool isNative = false)
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
        NativeLinkInputs = nativeLinkInputs ?? gflat.Projects.NativeLinkInputs.Empty;
        PreprocessorInputs = preprocessorInputs ?? gflat.Projects.PreprocessorInputs.Empty;
        RuntimeLibrary = runtimeLibrary;
        IsNative = isNative;
    }
    public static ProjectModel Load(string path) => Load(path, null, null);
    public static ProjectModel Load(string path, string? configuration, string? platform, int? exceptionReserveBytes = null, int? optimizationLevel = null, bool? noStdLib = null, bool asReference = false, bool? debugSymbols = null, bool? noBuiltins = null)
    {
        path = Path.GetFullPath(path);
        if (path.EndsWith(".vcxproj", StringComparison.OrdinalIgnoreCase)) return VisualStudioMsBuild.LoadProject(path, configuration, platform);
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
    public NativeLinkInputs NativeLinkInputs { get; }
    public PreprocessorInputs PreprocessorInputs { get; }
    public IReadOnlyList<string> BinaryInterfaces { get; }
    public string RuntimeLibrary { get; }
    public bool HasRuntimeLibrarySelection { get; }
    public bool RequiresVisualStudioMsBuild { get; }
    private ProjectGraph(ProjectModel root, List<ProjectModel> projects, List<string> sources, bool requiresVisualStudioMsBuild)
    {
        Root = root; Projects = projects; Sources = sources;
        RequiresVisualStudioMsBuild = requiresVisualStudioMsBuild;
        var byPath = projects.ToDictionary(project => project.FilePath, ProjectModel.Paths);
        var seen = new HashSet<string>(ProjectModel.Paths);
        IEnumerable<string> IncludeDirectories(ProjectModel project)
        {
            if (!seen.Add(project.FilePath)) yield break;
            if (project != root && !project.IsNative && project.Kind is ProjectKind.StaticLibrary or ProjectKind.DynamicLibrary) yield break;
            foreach (string directory in project.PreprocessorInputs.Directories) yield return directory;
            foreach (string reference in project.References)
                foreach (string directory in IncludeDirectories(byPath[reference])) yield return directory;
        }
        PreprocessorInputs = new(IncludeDirectories(root), root.PreprocessorInputs.Defines.Select(pair => pair.Key + "=" + pair.Value),
            projects.Where(project => !BinaryReference(project)).SelectMany(project => project.PreprocessorInputs.DeclarationScopes)
                .GroupBy(pair => pair.Key, ProjectModel.Paths).Select(group => group.First()));
        bool BinaryReference(ProjectModel project) => project != root && project.Kind is ProjectKind.StaticLibrary or ProjectKind.DynamicLibrary;
        BinaryInterfaces = projects.Where(project => BinaryReference(project) && !project.IsNative).Select(project => project.TargetPath! + ".gfi").ToArray();
        var profiles = projects.Where(project => BinaryReference(project)).Select(project => project.RuntimeLibrary ??
            (project.IsNative ? null : new BinaryProjectReference(project.TargetPath!, project.Kind == ProjectKind.DynamicLibrary).RuntimeLibrary))
            .Append(root.RuntimeLibrary).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (profiles.Length > 1) throw new ProjectException(root.FilePath, "Referenced libraries use incompatible RuntimeLibrary settings.");
        RuntimeLibrary = profiles.FirstOrDefault() ?? "MultiThreaded";
        HasRuntimeLibrarySelection = profiles.Length != 0;
        NativeLinkInputs = NativeLinkProjectOrder.Aggregate(root.FilePath, projects.Select(project =>
            new NativeLinkProject(project.FilePath, BinaryReference(project) ? [] : project.References,
                BinaryReference(project) && !project.IsNative ? new BinaryProjectReference(project.TargetPath!, project.Kind == ProjectKind.DynamicLibrary).NativeInputs : project.NativeLinkInputs)));
    }

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
        var evaluated = new Dictionary<string, ProjectModel>(ProjectModel.Paths);
        ProjectModel Visit(string file, bool asReference)
        {
            file = Path.GetFullPath(file);
            if (!active.Add(file)) throw new ProjectException(file, "Cyclic project reference.");
            var project = loader(file, asReference);
            evaluated[file] = project;
            if (asReference && project.Kind is ProjectKind.StaticLibrary or ProjectKind.DynamicLibrary)
            {
                active.Remove(file);
                visited.Add(file);
                projects.Add(project);
                return project;
            }
            foreach (string reference in project.References)
            {
                if (active.Contains(reference)) throw new ProjectException(file, "Cyclic project reference: " + reference);
                if (visited.Contains(reference)) continue;
                var dependency = Visit(reference, asReference: true);
                if (dependency.Kind == ProjectKind.Executable)
                    throw new ProjectException(file, "Executable projects cannot be referenced: " + reference);
            }
            active.Remove(file);
            visited.Add(file);
            projects.Add(project);
            foreach (string source in project.SourceFiles()) if (sourceSet.Add(source)) sources.Add(source);
            return project;
        }
        var root = Visit(path, asReference: false);
        var inspected = new HashSet<string>(ProjectModel.Paths);
        bool NeedsNativeEngine(ProjectModel project)
        {
            if (!inspected.Add(project.FilePath)) return false;
            if (project.IsNative) return true;
            foreach (string reference in project.References)
            {
                if (reference.EndsWith(".vcxproj", StringComparison.OrdinalIgnoreCase)) return true;
                if (!evaluated.TryGetValue(reference, out var dependency)) evaluated[reference] = dependency = loader(reference, true);
                if (NeedsNativeEngine(dependency)) return true;
            }
            return false;
        }
        return new ProjectGraph(root, projects, sources, NeedsNativeEngine(root));
    }
}
