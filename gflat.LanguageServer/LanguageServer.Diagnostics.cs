namespace gflat.LanguageServer;

public sealed partial class LanguageServer
{
    private sealed record PublishedDiagnostics(int? Version, EditorDiagnostic[] Diagnostics);
    private readonly Dictionary<string, PublishedDiagnostics> published = new(Workspace.Paths);

    private async Task PublishDiagnostics(Analysis analysis)
    {
        foreach (string path in published.Keys.Union(analysis.Diagnostics.Keys, Workspace.Paths).ToArray())
        {
            documents.TryGetValue(path, out var document);
            var diagnostics = analysis.Diagnostics.GetValueOrDefault(path) ?? [];
            var previous = published.GetValueOrDefault(path);
            if (document == null && diagnostics.Length == 0 && (previous?.Diagnostics.Length ?? 0) == 0)
            {
                published.Remove(path);
                continue;
            }
            if (previous?.Version == document?.Version && previous != null && SameDiagnostics(previous.Diagnostics, diagnostics))
                continue;
            await Send(new { jsonrpc = "2.0", method = "textDocument/publishDiagnostics", @params = new
            {
                uri = Workspace.FileUri(path), version = document?.Version, diagnostics
            } });
            if (document != null || diagnostics.Length != 0) published[path] = new(document?.Version, diagnostics);
            else published.Remove(path);
        }
    }

    private static bool SameDiagnostics(EditorDiagnostic[] left, EditorDiagnostic[] right)
    {
        if (left.Length != right.Length) return false;
        for (int index = 0; index < left.Length; index++)
        {
            var first = left[index];
            var second = right[index];
            if (first.Range != second.Range || first.Severity != second.Severity || first.Code != second.Code ||
                first.Source != second.Source || first.Message != second.Message ||
                !(first.RelatedInformation ?? []).SequenceEqual(second.RelatedInformation ?? [])) return false;
        }
        return true;
    }
}
