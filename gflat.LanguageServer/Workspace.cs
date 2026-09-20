using System.Text.Json;
using gflat.diagnostics;

namespace gflat.LanguageServer;

public sealed record OpenDocument(string Path, string Text, int Version);
public sealed record Position(int Line, int Character);
public sealed record TextRange(Position Start, Position End);
public sealed record Location(string Uri, TextRange Range);
public sealed record RelatedInformation(Location Location, string Message);
public sealed record EditorDiagnostic(TextRange Range, int Severity, string Code, string Source, string Message,
    RelatedInformation[]? RelatedInformation = null);
public sealed record Analysis(Dictionary<string, EditorDiagnostic[]> Diagnostics, string[] WatchedPaths);

public static class Workspace
{
    public const string ConfigurationName = "gflat-workspace.json";
    public static readonly StringComparer Paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    public static string FilePath(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || !parsed.IsFile)
            throw new ArgumentException("Only file: document URIs are supported");
        return Path.GetFullPath(parsed.LocalPath);
    }
    public static string FileUri(string path) => new Uri(Path.GetFullPath(path)).AbsoluteUri;

    public static Analysis Analyze(string? root, IReadOnlyDictionary<string, OpenDocument> documents)
    {
        var output = new Dictionary<string, List<EditorDiagnostic>>(Paths);
        var configured = new HashSet<string>(Paths);
        var watched = new HashSet<string>(Paths);
        string? config = root == null ? null : Path.Combine(root, ConfigurationName);
        if (config != null) watched.Add(config);
        if (config != null && File.Exists(config))
        {
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(config));
                var sources = json.RootElement.GetProperty("sources");
                if (sources.ValueKind != JsonValueKind.Array || sources.GetArrayLength() == 0)
                    throw new ArgumentException("'sources' must be a nonempty array of relative file paths");
                foreach (var item in sources.EnumerateArray())
                {
                    string relative = item.GetString() ?? throw new ArgumentException("Source paths must be strings");
                    if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.IndexOfAny(['*', '?']) >= 0)
                        throw new ArgumentException("Source paths must be relative paths without wildcards");
                    string path = Path.GetFullPath(Path.Combine(root!, relative));
                    if (!configured.Add(path)) throw new ArgumentException($"Duplicate source path: {relative}");
                    watched.Add(path);
                }
                Check(configured);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException)
            {
                output[config] = [Problem("GFLS0001", "Workspace configuration: " + error.Message)];
            }
        }
        foreach (var document in documents.Values)
            if (!configured.Contains(document.Path)) Check([document.Path]);
        return new(output.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), Paths), watched.ToArray());

        void Check(IEnumerable<string> paths)
        {
            var snapshots = new List<SourceFile>();
            bool unreadable = false;
            foreach (string path in paths)
            {
                output.TryAdd(path, new());
                try { snapshots.Add(new(path, documents.TryGetValue(path, out var open) ? open.Text : File.ReadAllText(path))); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    output[path].Add(Problem("GFLS0001", "Cannot read source: " + error.Message));
                    unreadable = true;
                }
            }
            if (unreadable || snapshots.Count == 0) return;
            try
            {
                foreach (var diagnostic in Compiler.Analyze(snapshots))
                {
                    string path = diagnostic.FilePath ?? snapshots[0].Path;
                    // Prelude failures are still visible on a real document, never as invalid file URIs.
                    if (path.StartsWith('<')) path = snapshots[0].Path;
                    output.TryAdd(path, new());
                    output[path].Add(Convert(diagnostic));
                }
            }
            catch (Exception error)
            {
                output[snapshots[0].Path].Add(Problem("GFLS0002", "Compiler analysis failed: " + error.Message));
            }
        }
    }

    public static EditorDiagnostic Problem(string code, string message) => new(new(new(0, 0), new(0, 0)), 1, code, "gflat", message);

    public static EditorDiagnostic Convert(Diagnostic diagnostic) => new(ToRange(diagnostic.Span),
        diagnostic.Severity switch { DiagnosticSeverity.Error => 1, DiagnosticSeverity.Warning => 2, _ => 3 },
        diagnostic.Descriptor.Id, "gflat", diagnostic.Message,
        diagnostic.RelatedLocations.Where(s => s.Source != null && !s.Source.Path.StartsWith('<'))
            .Select(s => new RelatedInformation(new(FileUri(s.Source!.Path), ToRange(s)), "Previous declaration")).ToArray());

    public static TextRange ToRange(SourceSpan span)
    {
        if (span.Source == null) return new(new(Math.Max(0, span.Line - 1), Math.Max(0, span.Column - 1)), new(Math.Max(0, span.Line - 1), Math.Max(0, span.Column - 1)));
        var start = span.Source.Span(span.Start);
        var end = span.Source.Span(span.Start + Math.Max(1, span.Length));
        return new(new(start.Line - 1, start.Column - 1), new(end.Line - 1, end.Column - 1));
    }

    public static string ApplyChanges(string text, JsonElement changes)
    {
        foreach (var change in changes.EnumerateArray())
        {
            string replacement = change.GetProperty("text").GetString()!;
            if (!change.TryGetProperty("range", out var range)) { text = replacement; continue; }
            int start = Offset(text, range.GetProperty("start")), end = Offset(text, range.GetProperty("end"));
            if (end < start) throw new ArgumentException("Invalid edit range");
            text = text[..start] + replacement + text[end..];
        }
        return text;
    }

    private static int Offset(string text, JsonElement position)
    {
        int line = position.GetProperty("line").GetInt32(), character = position.GetProperty("character").GetInt32();
        if (line < 0 || character < 0) throw new ArgumentException("Negative edit position");
        int start = 0;
        for (int i = 0; i < line; i++)
        {
            int newline = text.IndexOf('\n', start);
            if (newline < 0) throw new ArgumentException("Edit line is outside the document");
            start = newline + 1;
        }
        int end = text.IndexOf('\n', start);
        if (end < 0) end = text.Length;
        if (end > start && text[end - 1] == '\r') end--;
        if (character > end - start) throw new ArgumentException("Edit character is outside the line");
        return start + character; // .NET string indices and LSP's default positions are UTF-16.
    }
}
