namespace gflat.LanguageServer;

// A session retains one text version per source graph. Project evaluation and
// source reads still run before lookup, so disk and unsaved changes are observed.
internal sealed class WorkspaceAnalysisCache
{
    private sealed record Entry(SourceFile[] Sources, SourceFile[] AutomaticSources,
        CompilationOptions Options, AnalysisSnapshot Snapshot, EditorModel Model);

    private readonly List<Entry> entries = new();
    private readonly SourceTokenCache tokenCache = new();
    private readonly SourceSyntaxCache syntaxCache = new();

    public (AnalysisSnapshot Snapshot, EditorModel Model) Get(IReadOnlyList<SourceFile> sources, CompilationOptions? options,
        CancellationToken cancellationToken = default, IReadOnlySet<SourceId>? bodySources = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new();
        // Retain the inputs used for this analysis even when the caller updates
        // its configuration collections before requesting another snapshot.
        options = options with {
            IncludeDirectories = options.IncludeDirectories.ToArray(),
            ForeignCompilerArguments = options.ForeignCompilerArguments.ToArray(),
            BinaryInterfaces = options.BinaryInterfaces.ToArray(),
            ExportSources = options.ExportSources?.ToArray(),
            FileDeclarationScopes = options.FileDeclarationScopes.Count == 0 ? EmptyDeclarationScopes :
                new Dictionary<string, DeclarationScope>(options.FileDeclarationScopes, Workspace.Paths),
            Defines = options.Defines.Count == 0 ? EmptyDefines : new Dictionary<string, string>(options.Defines, StringComparer.Ordinal)
        };
        SourceFile[]? automatic = ReadAutomaticSources(options);
        for (int index = entries.Count - 1; index >= 0; index--)
        {
            Entry entry = entries[index];
            if (!SameOptions(entry.Options, options) || !SameSources(entry.Sources, sources, compareText: false)) continue;
            if (entry.Snapshot.BodySources != null && (bodySources == null || !entry.Snapshot.BodySources.SetEquals(bodySources))) continue;
            entries.RemoveAt(index);
            if (automatic != null && SameSources(entry.Sources, sources, compareText: true)
                && SameSources(entry.AutomaticSources, automatic, compareText: true) && SameIncludes(entry.Snapshot, options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                entries.Add(entry);
                return (entry.Snapshot, entry.Model);
            }
        }
        var snapshot = new AnalysisSnapshot(sources, options, cancellationToken, bodySources, tokenCache, syntaxCache: syntaxCache);
        var model = new EditorModel(snapshot, sources, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        // Automatic sources are read again because Compilation loads them itself.
        // Do not retain an entry if those files changed during construction.
        if (automatic != null && ReadAutomaticSources(options) is { } after
            && SameSources(automatic, after, compareText: true))
        {
            entries.Add(new(sources.ToArray(), automatic, options, snapshot, model));
            if (entries.Count > 4) entries.RemoveAt(0);
        }
        return (snapshot, model);
    }

    private static bool SameOptions(CompilationOptions left, CompilationOptions right) =>
        (left with { IncludeDirectories = [], ForeignCompilerArguments = [], BinaryInterfaces = [], ExportSources = null, Defines = EmptyDefines, FileDeclarationScopes = EmptyDeclarationScopes, IncludeLoader = null, DependencyObserver = null }) ==
        (right with { IncludeDirectories = [], ForeignCompilerArguments = [], BinaryInterfaces = [], ExportSources = null, Defines = EmptyDefines, FileDeclarationScopes = EmptyDeclarationScopes, IncludeLoader = null, DependencyObserver = null }) &&
        left.BinaryInterfaces.SequenceEqual(right.BinaryInterfaces, Workspace.Paths) &&
        (left.ExportSources == null ? right.ExportSources == null : right.ExportSources != null && left.ExportSources.SequenceEqual(right.ExportSources, Workspace.Paths)) &&
        left.FileDeclarationScopes.Count == right.FileDeclarationScopes.Count &&
        left.FileDeclarationScopes.All(pair => right.FileDeclarationScopes.TryGetValue(pair.Key, out var scope) && scope == pair.Value) &&
        left.ForeignCompilerArguments.SequenceEqual(right.ForeignCompilerArguments) &&
        left.IncludeDirectories.SequenceEqual(right.IncludeDirectories, Workspace.Paths) &&
        left.Defines.Count == right.Defines.Count && left.Defines.All(pair => right.Defines.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private static readonly IReadOnlyDictionary<string, string> EmptyDefines = new Dictionary<string, string>();
    private static readonly IReadOnlyDictionary<string, DeclarationScope> EmptyDeclarationScopes = new Dictionary<string, DeclarationScope>();

    private static bool SameIncludes(AnalysisSnapshot snapshot, CompilationOptions options)
    {
        try
        {
            var previous = snapshot.Sources.Where(source => !source.Path.StartsWith('<')).ToDictionary(source => Path.GetFullPath(source.Path), Workspace.Paths);
            foreach (string path in snapshot.PreprocessorDependencies.Where(path => !path.StartsWith('<')))
            {
                if (snapshot.ForeignDependencies.TryGetValue(path, out var foreign))
                {
                    if (!foreign.IsCurrent()) return false;
                    continue;
                }
                var current = options.IncludeLoader?.Invoke(path) ?? (File.Exists(path) ? new SourceFile(path, File.ReadAllText(path)) : null);
                if (previous.TryGetValue(path, out var source) ? current?.Text != source.Text : current != null) return false;
            }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    private static SourceFile[]? ReadAutomaticSources(CompilationOptions options)
    {
        if (options.NoStdLib) return [];
        try { return StandardLibrary.AutomaticFiles.Select(StandardLibrary.Load).ToArray(); }
        catch (gflat.CompileExceptions.TypeCheckException) { return null; }
    }

    private static bool SameSources(IReadOnlyList<SourceFile> left, IReadOnlyList<SourceFile> right, bool compareText)
    {
        if (left.Count != right.Count) return false;
        for (int index = 0; index < left.Count; index++)
            if (!string.Equals(left[index].Path, right[index].Path, StringComparison.Ordinal)
                || compareText && !string.Equals(left[index].Text, right[index].Text, StringComparison.Ordinal)) return false;
        return true;
    }
}
