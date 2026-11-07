using System.Text.Json;

namespace gflat.LanguageServer;

public sealed partial class LanguageServer
{
    private string[] readOnlySourceRoots = [];

    private void ConfigureReadOnlySources(JsonElement options)
    {
        if (options.ValueKind != JsonValueKind.Object || !options.TryGetProperty("readOnlySourceRoots", out var roots)) return;
        if (roots.ValueKind != JsonValueKind.Array) throw new ArgumentException("readOnlySourceRoots must be an array of absolute directory paths.");
        readOnlySourceRoots = roots.EnumerateArray().Select(root => {
            if (root.ValueKind != JsonValueKind.String || !Path.IsPathRooted(root.GetString()))
                throw new ArgumentException("readOnlySourceRoots must contain absolute directory paths.");
            return Path.GetFullPath(root.GetString()!).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }).Distinct(Workspace.Paths).ToArray();
    }

    private bool IsReadOnlySource(string path)
    {
        if (readOnlySourceRoots.Length == 0 || path.StartsWith('<')) return false;
        string fullPath = Path.GetFullPath(path);
        return readOnlySourceRoots.Any(root => fullPath.StartsWith(root,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
    }

    private bool WritableCodeAction(EditorCodeAction action) => action.Edit == null ||
        action.Edit.Changes.Keys.All(uri => !IsReadOnlySource(Workspace.FilePath(uri)));

    private bool ReadOnlyDeclaration(EditorModel model, string path, int offset) => readOnlySourceRoots.Length != 0 &&
        model.Definition(path, offset) is Location location && IsReadOnlySource(Workspace.FilePath(location.Uri));

    private object? PrepareRename(EditorModel model, string path, int offset)
        => ReadOnlyDeclaration(model, path, offset) ? null : model.PrepareRename(path, offset);
}
