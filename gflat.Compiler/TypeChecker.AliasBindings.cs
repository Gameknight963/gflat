using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private readonly Dictionary<string, AliasDeclaration> constrainedAliases = new();
    private readonly Dictionary<TypeExpression, string> aliasTargetNamespaces = new();
    private readonly Dictionary<TypeExpression, AliasDeclaration> aliasContracts = new();
    private readonly Dictionary<AliasDeclaration, List<TypeExpression>> boundAliasConstraints = new();
    private readonly HashSet<TypeExpression> selectedAliasTargets = new();
    private readonly HashSet<(AstNode Definition, string Arguments)> validatingAliasSpecializations = new();
    private AstNode? currentSpecializationDefinition;
    private AstNode? currentSpecializationBody;
    private sealed record GenericCallDependency(MethodDeclaration Definition, List<TypeExpression> TypeArguments);
    private readonly Dictionary<(AstNode Body, SourceSpan Call), GenericCallDependency> selectedGenericDependencies = new();
    private readonly Dictionary<(AstNode Body, SourceSpan Call), MethodDeclaration> selectedAliasCallTargets = new();
    private readonly Dictionary<(AstNode Definition, string Arguments), AstNode> aliasSpecializationBodies = new();
    private readonly HashSet<AstNode> unfinishedAliasProofBodies = new();
    private readonly Dictionary<AstNode, List<(AstNode Definition, List<GenericParameter> Parameters, List<TypeExpression> Arguments)>> pendingAliasProofs = new();

    private string AliasNominalArguments(IEnumerable<TypeExpression> arguments) => string.Join(";", arguments.Select(GetTypeMangledName));

    private void RegisterAliasSpecialization(AstNode definition, List<TypeExpression> arguments, AstNode body)
    {
        aliasSpecializationBodies[(definition, AliasNominalArguments(arguments))] = body;
        unfinishedAliasProofBodies.Add(body);
    }

    private void RecordAliasCallTarget(CallExpression call, MethodDeclaration method)
    {
        if (currentSpecializationBody != null) selectedAliasCallTargets[(currentSpecializationBody, call.Span)] = method;
    }

    private void RecordAliasGenericDependency(CallExpression call, MethodDeclaration method, List<TypeExpression> arguments)
    {
        if (currentSpecializationBody != null)
            selectedGenericDependencies[(currentSpecializationBody, call.Span)] = new(method, arguments.ToList());
    }

    private void CompleteAliasSpecializationBody(AstNode body, bool completed)
    {
        if (!unfinishedAliasProofBodies.Remove(body)) return;
        if (!pendingAliasProofs.Remove(body, out var pending) || !completed) return;
        foreach (var request in pending)
            AnalyzeSafely(() => ValidateAliasSpecialization(request.Definition, request.Parameters, request.Arguments));
    }

    // A previously emitted specialization may have been reached first through the
    // concrete provider. Check the contract view on every use before reusing it.
    private void ValidateAliasSpecialization(AstNode definition, List<GenericParameter> parameters, List<TypeExpression> arguments)
    {
        if (!arguments.Any(ContainsAliasContract) || parameters.Count != arguments.Count) return;
        var proof = aliasSpecializationBodies.GetValueOrDefault((definition, AliasNominalArguments(arguments)));
        if (proof != null && unfinishedAliasProofBodies.Contains(proof))
        {
            if (!pendingAliasProofs.TryGetValue(proof, out var pending)) pendingAliasProofs[proof] = pending = new();
            pending.Add((definition, parameters, arguments.ToList()));
            return;
        }
        var key = (definition, string.Join(";", arguments.Select(AliasContractArgumentKey)));
        if (!validatingAliasSpecializations.Add(key)) return;
        using var source = SourceContext.Enter(definition.Span);
        using var locals = IsolateDeclarationContext();
        var previous = _currentNamespace;
        string ns = declarationNamespaces.GetValueOrDefault(definition) ?? "";
        _currentNamespace = ns.Length == 0 ? _globalScope : ResolveNamespaceByName(ns)!;
        var map = parameters.Zip(arguments).ToDictionary(p => p.First.Name, p => p.Second);
        try
        {
            string name = definition switch {
                MethodDeclaration method => method.Name,
                ClassDeclaration cls => cls.Name,
                StructDeclaration str => str.Name,
                InterfaceDeclaration iface => iface.Name,
                _ => throw new InvalidOperationException("A provider specialization requires a generic declaration")
            };
            var clone = new AstCloner(map, name).CloneNode(definition);
            Walk(clone);
        }
        finally { _currentNamespace = previous; validatingAliasSpecializations.Remove(key); }

        void Walk(AstNode node)
        {
            if (node is BlockStatement block)
            {
                _localAliases.Push(new());
                try { foreach (var statement in block.Statements) Walk(statement); }
                finally { _localAliases.Pop(); }
                return;
            }
            if (node is AliasDeclaration alias)
            {
                if (_localAliases.Count > 0) _localAliases.Peek()[alias.Name] = alias.TargetType;
                return;
            }
            if (node is NamespaceAccessExpression access) ValidateAliasMember(access);
            if (node is CallExpression call)
            {
                var selected = proof == null ? null : selectedGenericDependencies.GetValueOrDefault((proof, call.Span));
                if (proof != null && call.Callee is NamespaceAccessExpression provider &&
                    selectedAliasCallTargets.TryGetValue((proof, call.Span), out var selectedMethod))
                    ValidateAliasMember(provider, selectedMethod);
                // Follow only dependencies of the checked emitted body. Guessing
                // a declaration here can inspect a loser or recursively expand
                // a generic call before normal specialization limits apply.
                MethodDeclaration? target = selected?.Definition;
                var dependencyArguments = selected?.TypeArguments.Select(RebindDependencyArgument).ToList() ?? [];
                if (target is { IsGeneric: true } && target.GenericParameters.Count == dependencyArguments.Count)
                {
                    for (int i = 0; i < target.GenericParameters.Count; i++)
                    {
                        var parameter = target.GenericParameters[i];
                        if (parameter.Constraint != null && !FunctionConstraintSatisfied(target, dependencyArguments[i],
                            BindGenericConstraint(target.GenericParameters, dependencyArguments, parameter.Constraint)))
                            throw new TypeCheckException($"Type '{DisplayTypeName(dependencyArguments[i])}' does not satisfy constraint '{DisplayTypeName(parameter.Constraint)}' for type parameter '{parameter.Name}' on function '{DisplayName(target.Name)}'", call.Span);
                    }
                    ValidateAliasSpecialization(target, target.GenericParameters, dependencyArguments);
                }
            }
            foreach (var property in node.GetType().GetProperties().Where(p => p.GetIndexParameters().Length == 0))
            {
                if (property.GetValue(node) is AstNode child) Walk(child);
                else if (property.GetValue(node) is IEnumerable<AstNode> children)
                    foreach (var childNode in children) Walk(childNode);
            }
        }

        TypeExpression RebindDependencyArgument(TypeExpression type)
        {
            if (type.GenericParameterView is { } view && ReferenceEquals(view.DeclaringDefinition, definition) &&
                map.TryGetValue(view.ParameterName, out var replacement)) return replacement.WithQualifiersFrom(type);
            var rebound = type switch
            {
                PointerTypeExpression p => new PointerTypeExpression(RebindDependencyArgument(p.Inner), p.IsNullable, p.Line, p.IsReadOnly),
                ManagedTypeExpression p => new ManagedTypeExpression(RebindDependencyArgument(p.Inner), p.IsNullable, p.Line,
                    p.IsReadOnly, p.Collector == null ? null : RebindDependencyArgument(p.Collector)) { UsesDefaultCollector = p.UsesDefaultCollector },
                ReferenceTypeExpression r => new ReferenceTypeExpression(RebindDependencyArgument(r.Inner), r.Line),
                ArrayTypeExpression a => new ArrayTypeExpression(RebindDependencyArgument(a.ElementType), a.Size, a.Line, a.SizeExpression),
                FunctionPointerTypeExpression f => new FunctionPointerTypeExpression(RebindDependencyArgument(f.ReturnType),
                    f.ParameterTypes.Select(RebindDependencyArgument).ToList(), f.IsManaged, f.IsNullable, f.Line, f.IsSignature),
                NamedTypeExpression n when n.GenericUse is { } use => new NamedTypeExpression(use.Name, use.Namespace, n.Line,
                    use.TypeArguments.Select(RebindDependencyArgument).ToList()),
                NamedTypeExpression n when n.TypeArguments.Count > 0 => new NamedTypeExpression(n.Name, n.Namespace, n.Line,
                    n.TypeArguments.Select(RebindDependencyArgument).ToList()),
                _ => type
            };
            return rebound.WithQualifiersFrom(type);
        }
    }

    private string AliasContractArgumentKey(TypeExpression type)
    {
        var aliases = new List<string>();
        Visit(type);
        return GetTypeMangledName(type) + "[" + string.Join(",", aliases) + "]";
        void Visit(TypeExpression child)
        {
            if (aliasContracts.TryGetValue(child, out var alias)) aliases.Add(
                (alias.Span.SourceId?.Path ?? "") + ":" + alias.Span.Start + ":" + alias.Name);
            switch (child)
            {
                case PointerTypeExpression pointer: Visit(pointer.Inner); break;
                case ManagedTypeExpression managed:
                    Visit(managed.Inner);
                    if (managed.Collector != null) Visit(managed.Collector);
                    break;
                case ReferenceTypeExpression reference: Visit(reference.Inner); break;
                case ArrayTypeExpression array: Visit(array.ElementType); break;
                case FunctionPointerTypeExpression function:
                    Visit(function.ReturnType);
                    foreach (var parameter in function.ParameterTypes) Visit(parameter);
                    break;
                case NamedTypeExpression named:
                    foreach (var argument in named.GenericUse?.TypeArguments ?? named.TypeArguments) Visit(argument);
                    break;
            }
        }
    }

    private bool ContainsAliasContract(TypeExpression type)
    {
        if (aliasContracts.ContainsKey(type)) return true;
        return type switch
        {
            PointerTypeExpression p => ContainsAliasContract(p.Inner),
            ManagedTypeExpression p => ContainsAliasContract(p.Inner) || p.Collector != null && ContainsAliasContract(p.Collector),
            ReferenceTypeExpression r => ContainsAliasContract(r.Inner),
            ArrayTypeExpression a => ContainsAliasContract(a.ElementType),
            FunctionPointerTypeExpression f => ContainsAliasContract(f.ReturnType) || f.ParameterTypes.Any(ContainsAliasContract),
            NamedTypeExpression n => n.TypeArguments.Any(ContainsAliasContract) ||
                n.GenericUse != null && n.GenericUse.TypeArguments.Any(ContainsAliasContract),
            _ => false
        };
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
            return declaration.Constraints.Any(c => TypesMatch(ResolveAlias(c), constraint) ||
                constraint is NamedTypeExpression named &&
                (GetClassByIdentity(named.Name) != null && TypeDerivesFromClass(ResolveAlias(c), named.Name) ||
                 GetInterface(named.Name) != null && TypeImplementsInterface(ResolveAlias(c), named.Name)));
        }
        finally { _currentNamespace = previous; }
    }

    private TypeExpression ResolveAliasBindingTarget(TypeExpression target, Action<TypeExpression>? publish = null)
    {
        if (!aliasTargetNamespaces.TryGetValue(target, out var ns)) return ResolveAlias(target, publish);
        using var source = SourceContext.Enter(target.Span);
        using var locals = IsolateDeclarationContext();
        var previous = _currentNamespace;
        _currentNamespace = ns.Length == 0 ? _globalScope : ResolveNamespaceByName(ns)!;
        try
        {
            var result = ResolveAlias(target, publish == null ? null : value => publish(ContractView(value)));
            return ContractView(result);
        }
        finally { _currentNamespace = previous; }

        TypeExpression ContractView(TypeExpression result)
        {
            if (aliasContracts.TryGetValue(target, out var contract) && result is NamedTypeExpression named)
            {
                // Contract views belong to this alias use, not the shared implementation.
                result = new NamedTypeExpression(named.Name, named.Namespace, target.Line, named.TypeArguments)
                    { Span = target.Span };
                if (named.IsResolvedNominal) result = ((NamedTypeExpression)result).AsResolvedNominal();
                result = result.WithQualifiersFrom(named);
                aliasContracts[result] = contract;
            }
            return result;
        }
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
            AnalyzeSafely(() =>
            {
                var implementation = ResolveAliasBindingTarget(alias.TargetType);
                var constraints = new List<TypeExpression>();
                foreach (var constraint in alias.Constraints)
                    if (!TypeSatisfiesDeclaredConstraint(alias, implementation, constraint, constraints.Add))
                        throw new TypeCheckException($"Implementation of alias '{alias.Name}' does not satisfy '{TypeName(constraint)}'", alias.Span);
                boundAliasConstraints[alias] = constraints;
            });
        }
        // Diagnose cycles even when an alias is never used by a body.
        foreach (var target in selectedAliasTargets.ToArray())
            AnalyzeSafely(() => ResolveAliasBindingTarget(target));
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
                IEnumerable<MethodDeclaration> methods = resolved is NamedTypeExpression type
                    ? GetInterface(type.Name)?.Methods ??
                        GetClassByIdentity(type.Name)?.Methods.Values.SelectMany(m =>
                            overloadFamilies.GetValueOrDefault(m.Method) ?? [m.Method])
                            .Where(m => m.IsStatic && m.Accessibility == TokenKind.Public) ?? []
                    : [];
                if (methods.Any(m => (m.Name == access.Member || m.PropertyName == access.Member) &&
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
