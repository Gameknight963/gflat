using System.Buffers;

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
        string? ownerName = _currentClass?.Name ?? _currentStruct?.Name;
        if (ownerName == null) return null;
        ReadOnlySpan<char> owner = ownerName;
        int length = owner.Length + name.Length + 1;
        char[]? rented = length > 256 ? ArrayPool<char>.Shared.Rent(length) : null;
        Span<char> buffer = rented == null ? stackalloc char[length] : rented;
        try
        {
            while (!owner.IsEmpty)
            {
                owner.CopyTo(buffer);
                buffer[owner.Length] = '.';
                name.AsSpan().CopyTo(buffer[(owner.Length + 1)..]);
                ReadOnlySpan<char> candidate = buffer[..(owner.Length + name.Length + 1)];
                if (_classes.GetAlternateLookup<ReadOnlySpan<char>>().ContainsKey(candidate) ||
                    _structs.GetAlternateLookup<ReadOnlySpan<char>>().ContainsKey(candidate) ||
                    _interfaces.GetAlternateLookup<ReadOnlySpan<char>>().ContainsKey(candidate) ||
                    _enums.GetAlternateLookup<ReadOnlySpan<char>>().ContainsKey(candidate) ||
                    _genericClasses.GetAlternateLookup<ReadOnlySpan<char>>().ContainsKey(candidate) ||
                    _genericStructs.GetAlternateLookup<ReadOnlySpan<char>>().ContainsKey(candidate) ||
                    _symbols.GenericInterfaces.GetAlternateLookup<ReadOnlySpan<char>>().ContainsKey(candidate))
                    return candidate.ToString();
                int separator = owner.LastIndexOf('.');
                owner = separator < 0 ? default : owner[..separator];
            }
            return null;
        }
        finally
        {
            if (rented != null) ArrayPool<char>.Shared.Return(rented);
        }
    }
}
