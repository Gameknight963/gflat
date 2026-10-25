using gflat.ast;

namespace gflat;

public partial class TypeChecker
{
    public NamedTypeExpression? GetGenericTypeOrigin(string identity) => extensionGenericTypeOrigins.GetValueOrDefault(identity);
    public bool IsResolvedTypeIdentity(TypeExpression type) => type is NamedTypeExpression { IsResolvedNominal: true };

    // Use the checker's chosen arguments: protection in a formal parameter can
    // erase distinctions that cannot be recovered from the closed signature.
    public IReadOnlyList<TypeExpression>? GetGenericCallTypeArguments(CallExpression call) =>
        extensionCalls.TryGetValue(call, out var lowered) ? GetGenericCallTypeArguments(lowered) :
        selectedGenericCallArguments.GetValueOrDefault(call);

    // Preserve a source parameter as itself. The caller supplies its already
    // known, nongeneric interface; this query never binds or specializes a type.
    public TypeExpression? CreateOpenGenericParameterView(AstNode definition, string name, TypeExpression? knownConstraint)
    {
        var parameters = definition switch {
            MethodDeclaration method => method.GenericParameters,
            ClassDeclaration type => type.GenericParameters,
            StructDeclaration type => type.GenericParameters,
            InterfaceDeclaration type => type.GenericParameters,
            _ => null
        };
        int index = parameters?.FindIndex(parameter => parameter.Name == name) ?? -1;
        if (index < 0 || (parameters![index].Constraint == null) != (knownConstraint == null)) return null;
        if (knownConstraint != null && (knownConstraint is not NamedTypeExpression { Namespace: null, TypeArguments.Count: 0 } contract ||
            !_interfaces.ContainsKey(contract.Name))) return null;
        var source = NamedTypeExpression.Resolved(name, parameters[index].Line);
        return source.WithGenericParameterView(new GenericParameterContractView(
            ComputeGenericParameterOrigin(definition, index), definition, name, source,
            knownConstraint == null ? [] : [knownConstraint], IsOpen: true));
    }

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
