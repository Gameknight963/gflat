using gflat.ast;

namespace gflat;

public partial class TypeChecker
{
    public NamedTypeExpression? GetGenericTypeOrigin(string identity) => extensionGenericTypeOrigins.GetValueOrDefault(identity);
    public bool IsResolvedTypeIdentity(TypeExpression type) => type is NamedTypeExpression { IsResolvedNominal: true };

    // Editor queries supply their own context and never change checker state.
    public string? ResolveVisibleNamespacePath(string name, string namespaceName, IEnumerable<string> imports)
    {
        var scope = ResolveVisibleNamespaceByName(name, ExtensionNamespace(namespaceName) ?? _globalScope,
            imports, reportAmbiguity: false);
        if (scope == null) return null;
        var parts = new Stack<string>();
        while (scope.Parent is { } parent)
        {
            parts.Push(parent.Children.First(child => ReferenceEquals(child.Value, scope)).Key);
            scope = parent;
        }
        return string.Join("::", parts);
    }
}
