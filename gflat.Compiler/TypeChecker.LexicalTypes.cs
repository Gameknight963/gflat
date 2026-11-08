namespace gflat;

public partial class TypeChecker
{
    private readonly Dictionary<string, string[]> namespacePathSegments = new(StringComparer.Ordinal);

    private string[] NamespacePathSegments(string name)
    {
        if (!namespacePathSegments.TryGetValue(name, out var segments))
        {
            segments = name.Split(new[] { "::" }, StringSplitOptions.RemoveEmptyEntries);
            namespacePathSegments.Add(name, segments);
        }
        return segments;
    }

    private bool HasLocalAlias(string name)
    {
        foreach (var scope in _localAliases)
            if (scope.ContainsKey(name)) return true;
        return false;
    }

    // Only declarations inside the current type and its enclosing types belong
    // here. A global fallback would incorrectly outrank namespace aliases.
    private string? ResolveEnclosingNestedTypeName(string name)
    {
        if (name.Contains('.') || name.Contains("::") || name.Contains('$')) return null;
        string? owner = _currentClass?.Name ?? _currentStruct?.Name;
        while (owner != null)
        {
            string candidate = owner + "." + name;
            if (_classes.ContainsKey(candidate) || _structs.ContainsKey(candidate) ||
                _interfaces.ContainsKey(candidate) || _enums.ContainsKey(candidate) ||
                _genericClasses.ContainsKey(candidate) || _genericStructs.ContainsKey(candidate) || _symbols.GenericInterfaces.ContainsKey(candidate)) return candidate;
            int separator = owner.LastIndexOf('.');
            owner = separator < 0 ? null : owner[..separator];
        }
        return null;
    }
}
