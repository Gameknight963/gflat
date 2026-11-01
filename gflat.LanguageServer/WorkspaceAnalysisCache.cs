namespace gflat.LanguageServer;

// A session retains one text version per source graph. Project evaluation and
// source reads still run before lookup, so disk and unsaved changes are observed.
internal sealed class WorkspaceAnalysisCache
{
    private sealed record Entry(SourceFile[] Sources, SourceFile[] AutomaticSources,
        CompilationOptions Options, AnalysisSnapshot Snapshot, EditorModel Model);

    private readonly List<Entry> entries = new();

    public (AnalysisSnapshot Snapshot, EditorModel Model) Get(IReadOnlyList<SourceFile> sources, CompilationOptions? options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new();
        SourceFile[]? automatic = ReadAutomaticSources(options);
        for (int index = entries.Count - 1; index >= 0; index--)
        {
            Entry entry = entries[index];
            if (entry.Options != options || !SameSources(entry.Sources, sources, compareText: false)) continue;
            entries.RemoveAt(index);
            if (automatic != null && SameSources(entry.Sources, sources, compareText: true)
                && SameSources(entry.AutomaticSources, automatic, compareText: true))
            {
                cancellationToken.ThrowIfCancellationRequested();
                entries.Add(entry);
                return (entry.Snapshot, entry.Model);
            }
        }
        var snapshot = new AnalysisSnapshot(sources, options, cancellationToken);
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
