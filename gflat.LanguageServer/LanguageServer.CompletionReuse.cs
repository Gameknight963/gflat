namespace gflat.LanguageServer;

public sealed partial class LanguageServer
{
    private bool TryReuseCompletion(EditorRequest query, out object? result)
    {
        result = null;
        if (latestDocuments == null || latestProjectRevision != projectRevision || latestDocuments.Count != documents.Count) return false;
        string path = Workspace.FilePath(query.Parameters.GetProperty("textDocument").GetProperty("uri").GetString()!);
        foreach (var (otherPath, previous) in latestDocuments)
            if (!documents.TryGetValue(otherPath, out var current) || !Workspace.Paths.Equals(otherPath, path) && current.Text != previous.Text) return false;
        if (!documents.TryGetValue(path, out var document) || latest?.Models?.GetValueOrDefault(path) is not EditorModel model) return false;
        int offset = Workspace.Offset(document.Text, query.Parameters.GetProperty("position"));
        try
        {
            return model.TryReuseMemberCompletion(path, document.Text, offset, out result) ||
                model.TryReuseIdentifierCompletion(path, document.Text, offset, out result);
        }
        catch (OperationCanceledException) { return false; }
    }
}
