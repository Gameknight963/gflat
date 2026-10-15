using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private readonly Dictionary<string, AliasDeclaration> constrainedAliases = new();
    private readonly Dictionary<TypeExpression, string> aliasTargetNamespaces = new();
    private readonly Dictionary<TypeExpression, AliasDeclaration> aliasContracts = new();
    private readonly HashSet<TypeExpression> selectedAliasTargets = new();
    private readonly HashSet<string> validatingAliasSpecializations = new();

    // A previously emitted specialization may have been reached first through the
    // concrete provider. Check the contract view on every use before reusing it.
    private void ValidateAliasSpecialization(AstNode definition, List<GenericParameter> parameters, List<TypeExpression> arguments)
    {
        if (!arguments.Any(aliasContracts.ContainsKey) || parameters.Count != arguments.Count) return;
        string key = definition.Span.SourceId + ":" + definition.Span.Start + ":" +
            string.Join(";", arguments.Select(a => GetTypeMangledName(a) + (aliasContracts.TryGetValue(a, out var c) ? ":" + c.Name : "")));
        if (!validatingAliasSpecializations.Add(key)) return;
        using var source = SourceContext.Enter(definition.Span);
        using var locals = IsolateDeclarationContext();
        var previous = _currentNamespace;
        string ns = declarationNamespaces.GetValueOrDefault(definition) ?? "";
        _currentNamespace = ns.Length == 0 ? _globalScope : ResolveNamespaceByName(ns)!;
        try
        {
            var map = parameters.Zip(arguments).ToDictionary(p => p.First.Name, p => p.Second);
            var clone = new AstCloner(map).CloneNode(definition);
            Walk(clone);
        }
        finally { _currentNamespace = previous; validatingAliasSpecializations.Remove(key); }

        void Walk(AstNode node)
        {
            if (node is NamespaceAccessExpression access) ValidateAliasMember(access);
            if (node is CallExpression { TypeArguments.Count: > 0, Callee: IdentifierExpression id } call &&
                ResolveGenericFunction(id.Name) is { IsGeneric: true } target)
                ValidateAliasSpecialization(target, target.GenericParameters, call.TypeArguments);
            foreach (var property in node.GetType().GetProperties().Where(p => p.GetIndexParameters().Length == 0))
            {
                if (property.GetValue(node) is AstNode child) Walk(child);
                else if (property.GetValue(node) is IEnumerable<AstNode> children)
                    foreach (var childNode in children) Walk(childNode);
            }
        }
    }

    private TypeExpression ResolveGlobalAliasBinding(string name)
    {
        if (!_globalScope.Aliases.TryGetValue(name, out var target))
            throw new TypeCheckException($"Type alias '{name}' has no implementation", SourceContext.Current ?? SourceContext.ForLine(1));
        return ResolveAliasBindingTarget(target);
    }

    private bool AliasExposesConstraint(TypeExpression argument, TypeExpression constraint)
    {
        if (!aliasContracts.TryGetValue(argument, out var declaration)) return true;
        using var source = SourceContext.Enter(declaration.Span);
        using var locals = IsolateDeclarationContext();
        var previous = _currentNamespace;
        string ns = declarationNamespaces.GetValueOrDefault(declaration) ?? "";
        _currentNamespace = ns.Length == 0 ? _globalScope : ResolveNamespaceByName(ns)!;
        try
        {
            return declaration.Constraints.Any(c => TypesMatch(ResolveAlias(c), constraint));
        }
        finally { _currentNamespace = previous; }
    }

    private TypeExpression ResolveAliasBindingTarget(TypeExpression target)
    {
        if (!aliasTargetNamespaces.TryGetValue(target, out var ns)) return ResolveAlias(target);
        using var source = SourceContext.Enter(target.Span);
        using var locals = IsolateDeclarationContext();
        var previous = _currentNamespace;
        _currentNamespace = ns.Length == 0 ? _globalScope : ResolveNamespaceByName(ns)!;
        try
        {
            var result = ResolveAlias(target);
            if (aliasContracts.TryGetValue(target, out var contract) && result is NamedTypeExpression named)
            {
                // Keep the contract on this use, rather than on the shared concrete
                // declaration. Naming the implementation directly still exposes it.
                result = new NamedTypeExpression(named.Name, named.Namespace, target.Line, named.TypeArguments) { Span = target.Span };
                aliasContracts[result] = contract;
            }
            return result;
        }
        finally { _currentNamespace = previous; }
    }

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
                declarationNamespaces[replacement.Alias] = group.Key.Scope;
                replacement.Members.Remove(replacement.Alias);
            }
            else if (original.Alias.ImplementationKind == FunctionImplementationKind.Expect)
                throw new TypeCheckException($"Expected alias '{group.Key.Name}' has no implementation; provide replace alias", original.Alias.Span);
            selectedAliasTargets.Add(original.Alias.TargetType);
            if (original.Alias.Constraints.Count != 0)
            {
                constrainedAliases[Join(group.Key.Scope, group.Key.Name)] = original.Alias;
                aliasContracts[original.Alias.TargetType] = original.Alias;
            }
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
        {
            var implementation = ResolveAliasBindingTarget(alias.TargetType);
            foreach (var constraint in alias.Constraints)
                if (!TypeSatisfiesDeclaredConstraint(alias, implementation, constraint))
                    throw new TypeCheckException($"Implementation of alias '{alias.Name}' does not satisfy '{TypeName(constraint)}'", alias.Span);
        }
        // Diagnose cycles even when an alias is never used by a body.
        foreach (var target in selectedAliasTargets.ToArray()) ResolveAliasBindingTarget(target);
    }

    private void ValidateAliasMember(NamespaceAccessExpression access, MethodDeclaration? selectedMethod = null)
    {
        var seen = new HashSet<TypeExpression>();
        AliasDeclaration? declaration = FindContract(access.Left);
        if (declaration == null) return;
        foreach (var constraint in declaration.Constraints)
        {
            using var context = SourceContext.Enter(declaration.Span);
            using var locals = IsolateDeclarationContext();
            var previous = _currentNamespace;
            string ns = declarationNamespaces.GetValueOrDefault(declaration) ?? "";
            _currentNamespace = ns.Length == 0 ? _globalScope : ResolveNamespaceByName(ns)!;
            try
            {
                var resolved = ResolveAlias(constraint);
                if (resolved is NamedTypeExpression type && GetInterface(type.Name) is { } contract &&
                    contract.Methods.Any(m => (m.Name == access.Member || m.PropertyName == access.Member) &&
                        (selectedMethod == null || m.Name == selectedMethod.Name &&
                            m.Parameters.Count == selectedMethod.Parameters.Count &&
                            m.Parameters.Zip(selectedMethod.Parameters).All(p => TypesMatch(p.First.Type, p.Second.Type)) &&
                            TypesMatch(m.ReturnType, selectedMethod.ReturnType)))) return;
            }
            finally { _currentNamespace = previous; }
        }
        throw new TypeCheckException($"Member '{access.Member}' is not exposed by alias '{declaration.Name}'", access.Span);

        AliasDeclaration? FindContract(AstNode node)
        {
            if (node is TypeExpression t && aliasContracts.TryGetValue(t, out var recorded)) return recorded;
            string? name = Path(node);
            if (name == null) return null;
            TypeExpression? target = null;
            if (!name.Contains("::"))
                foreach (var local in _localAliases)
                    if (local.TryGetValue(name, out target)) break;
            if (target == null)
            {
                var scope = _currentNamespace;
                while (scope != null)
                {
                    string prefix = ScopePath(scope);
                    if (constrainedAliases.TryGetValue(prefix.Length == 0 ? name : prefix + "::" + name, out var direct)) return direct;
                    if (!name.Contains("::") && scope.Aliases.TryGetValue(name, out target)) break;
                    scope = scope.Parent;
                }
            }
            if (target == null && name.LastIndexOf("::", StringComparison.Ordinal) is var separator && separator >= 0)
            {
                var ns = ResolveNamespaceByName(name[..separator]);
                ns?.Aliases.TryGetValue(name[(separator + 2)..], out target);
            }
            if (target == null)
                foreach (var imported in CurrentUsings)
                    if (ResolveNamespaceByName(imported)?.Aliases.TryGetValue(name, out target) == true) break;
            if (target == null || !seen.Add(target)) return null;
            if (aliasContracts.TryGetValue(target, out var contract)) return contract;
            var previous = _currentNamespace;
            if (aliasTargetNamespaces.TryGetValue(target, out var bindingNamespace))
                _currentNamespace = bindingNamespace.Length == 0 ? _globalScope : ResolveNamespaceByName(bindingNamespace)!;
            try { return FindContract(target); }
            finally { _currentNamespace = previous; }
        }

        static string ScopePath(NamespaceScope scope) => scope.Parent == null ? "" :
            (ScopePath(scope.Parent) is { Length: > 0 } prefix ? prefix + "::" : "") + scope.Parent.Children.First(p => ReferenceEquals(p.Value, scope)).Key;
        static string? Path(AstNode node) => node switch
        {
            IdentifierExpression id => id.Name,
            NamespaceAccessExpression n when Path(n.Left) is { } parent => parent + "::" + n.Member,
            NamedTypeExpression t => t.Namespace == null ? t.Name : t.Namespace + "::" + t.Name,
            NestedTypeExpression t when Path(t.Parent) is { } parent => parent + "::" + t.Member,
            _ => null
        };
    }
}
