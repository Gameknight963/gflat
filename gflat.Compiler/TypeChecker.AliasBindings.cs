using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private readonly Dictionary<string, AliasDeclaration> constrainedAliases = new();

    private void SelectAliasBindings(CompilationUnit unit)
    {
        var entries = new List<(AliasDeclaration Alias, List<AstNode> Members, string Scope)>();
        Collect(unit.Members, "");
        foreach (var ns in unit.Namespaces) Collect(ns.Members, ns.Name);
        foreach (var group in entries.GroupBy(e => (e.Scope, e.Alias.Name)))
        {
            var declarations = group.Where(e => e.Alias.ImplementationKind != FunctionImplementationKind.Replace).ToList();
            var replacements = group.Where(e => e.Alias.ImplementationKind == FunctionImplementationKind.Replace).ToList();
            if (declarations.Count != 1 || replacements.Count > 1)
                throw new TypeCheckException($"Alias '{group.Key.Name}' requires exactly one declaration and at most one replacement", group.First().Alias.Span);
            var original = declarations[0];
            bool replaceable = original.Alias.ImplementationKind is FunctionImplementationKind.Weak or FunctionImplementationKind.Expect;
            if (replacements.Count != 0)
            {
                var replacement = replacements[0];
                if (!replaceable)
                    throw new TypeCheckException($"Alias '{group.Key.Name}' is not weak or expect", replacement.Alias.Span);
                if (replacement.Alias.Constraints.Count != 0)
                    throw new TypeCheckException("A replacement inherits its alias contract and cannot redeclare constraints", replacement.Alias.Span);
                original.Alias.TargetType = replacement.Alias.TargetType;
                replacement.Members.Remove(replacement.Alias);
            }
            else if (original.Alias.ImplementationKind == FunctionImplementationKind.Expect)
                throw new TypeCheckException($"Expected alias '{group.Key.Name}' has no implementation; provide replace alias", original.Alias.Span);
            if (original.Alias.Constraints.Count != 0)
                constrainedAliases[Join(group.Key.Scope, group.Key.Name)] = original.Alias;
        }
        void Collect(List<AstNode> members, string scope)
        {
            foreach (var node in members)
            {
                if (node is AliasDeclaration alias) entries.Add((alias, members, scope));
                if (node is NamespaceDeclaration ns) Collect(ns.Members, Join(scope, ns.Name));
            }
        }
        static string Join(string scope, string name) => scope.Length == 0 ? name : scope + "::" + name;
    }

    private void ValidateAliasContracts()
    {
        foreach (var alias in constrainedAliases.Values)
            foreach (var constraint in alias.Constraints)
                if (!TypeSatisfiesDeclaredConstraint(alias, alias.TargetType, constraint))
                    throw new TypeCheckException($"Implementation of alias '{alias.Name}' does not satisfy '{TypeName(constraint)}'", alias.Span);
    }

    private void ValidateAliasMember(NamespaceAccessExpression access)
    {
        string? name = Path(access.Left);
        if (name == null) return;
        AliasDeclaration? declaration = null;
        var scope = _currentNamespace;
        while (scope != null)
        {
            string prefix = ScopePath(scope);
            if (constrainedAliases.TryGetValue(prefix.Length == 0 ? name : prefix + "::" + name, out declaration)) break;
            scope = scope.Parent;
        }
        if (declaration == null)
            foreach (var imported in CurrentUsings)
                if (constrainedAliases.TryGetValue(imported + "::" + name, out declaration)) break;
        if (declaration == null) return;
        foreach (var constraint in declaration.Constraints)
        {
            var resolved = ResolveAlias(constraint);
            if (resolved is NamedTypeExpression type && GetInterface(type.Name) is { } contract &&
                contract.Methods.Any(m => m.Name == access.Member || m.PropertyName == access.Member)) return;
        }
        throw new TypeCheckException($"Member '{access.Member}' is not exposed by alias '{declaration.Name}'", access.Span);

        static string ScopePath(NamespaceScope scope) => scope.Parent == null ? "" :
            (ScopePath(scope.Parent) is { Length: > 0 } prefix ? prefix + "::" : "") + scope.Parent.Children.First(p => ReferenceEquals(p.Value, scope)).Key;
        static string? Path(AstNode node) => node switch
        {
            IdentifierExpression id => id.Name,
            NamespaceAccessExpression n when Path(n.Left) is { } parent => parent + "::" + n.Member,
            NamedTypeExpression t => t.Namespace == null ? t.Name : t.Namespace + "::" + t.Name,
            _ => null
        };
    }
}
