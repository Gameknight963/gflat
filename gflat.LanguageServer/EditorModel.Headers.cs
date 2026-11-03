using gflat.ast;

namespace gflat.LanguageServer;

public sealed record HeaderContext(string Id, string Label);

public sealed partial class EditorModel
{
    private readonly IReadOnlyDictionary<string, string> headerSelections;
    private readonly IReadOnlyList<AnalysisSnapshot.Syntax> activeNodes;

    private static HeaderContext? InclusionContext(AnalysisSnapshot.Syntax syntax, string path)
    {
        var parent = Parents(syntax).FirstOrDefault(p => p.Span.Source is { } source && !Workspace.Paths.Equals(source.Path, path) &&
            p.Node is ClassDeclaration or StructDeclaration or InterfaceDeclaration or NamespaceDeclaration or MethodDeclaration or CompilationUnit or ExtensionDeclaration);
        if (parent?.Span.Source is not { } root) return null;
        string scope = string.Join("::", Parents(parent).Reverse().Append(parent).Select(p => p.Node switch
        {
            ClassDeclaration c => c.SourceName, StructDeclaration s => s.SourceName, InterfaceDeclaration i => i.SourceName,
            NamespaceDeclaration n => n.Name, MethodDeclaration m => m.Name, _ => ""
        }).Where(name => name.Length != 0));
        return new(root.Path + "|" + scope, Path.GetFileName(root.Path) + (scope.Length == 0 ? "" : ": " + scope));
    }

    public HeaderContext[] HeaderContexts(string path) => snapshot.Nodes.Where(n => n.Span.Source?.Path == path)
        .Select(n => InclusionContext(n, path)).OfType<HeaderContext>().DistinctBy(c => c.Id).OrderBy(c => c.Label).ToArray();

    public EditorModel WithHeaderContext(string path, string id)
    {
        if (!HeaderContexts(path).Any(c => c.Id == id)) throw new ArgumentException("The header inclusion context is no longer available.");
        var selected = new Dictionary<string, string>(headerSelections, Workspace.Paths) { [path] = id };
        return new(snapshot, sources.Values, headerContexts: selected);
    }

    private object? IncludeCompletion(string path, int offset)
    {
        string text = sources[path].Text;
        int start = offset == 0 ? 0 : text.LastIndexOf('\n', offset - 1) + 1;
        string prefix = text[start..offset];
        var match = System.Text.RegularExpressions.Regex.Match(prefix, "^\\s*#\\s*include\\s*[\"<]([^\">]*)$");
        if (!match.Success) return null;
        string input = match.Groups[1].Value.Replace('\\', '/');
        int separator = input.LastIndexOf('/');
        string directoryPart = separator < 0 ? "" : input[..(separator + 1)];
        string partial = input[(separator + 1)..];
        int replacementEnd = offset;
        while (replacementEnd < text.Length && text[replacementEnd] is not ('"' or '>' or '\r' or '\n')) replacementEnd++;
        var replacement = Workspace.ToRange(sources[path].Span(offset - partial.Length, replacementEnd - offset + partial.Length));
        bool quoted = prefix.Contains('"');
        var roots = (quoted ? new[] { Path.GetDirectoryName(path)! } : Array.Empty<string>()).Concat(snapshot.Options.IncludeDirectories);
        var entries = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (string root in roots)
        {
            try
            {
                string folder = Path.GetFullPath(Path.Combine(root, directoryPart));
                if (!Directory.Exists(folder)) continue;
                foreach (string entry in Directory.EnumerateFileSystemEntries(folder))
                {
                    string name = Path.GetFileName(entry);
                    if (!name.StartsWith(partial, StringComparison.OrdinalIgnoreCase)) continue;
                    bool directory = Directory.Exists(entry);
                    string label = name + (directory ? "/" : "");
                    entries.TryAdd(label, new { label, kind = directory ? 19 : 17, textEdit = new { range = replacement, newText = label } });
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        return new { isIncomplete = false, items = entries.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase).Select(e => e.Value).ToArray() };
    }
}
