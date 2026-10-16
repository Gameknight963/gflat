using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    public enum ExtensionReceiverAdaptation { Exact, RawPointerPointee }
    public sealed record ExtensionMethodCandidate(MethodDeclaration Definition,
        ExtensionReceiverAdaptation Adaptation, bool IsReferenceReceiver,
        IReadOnlyDictionary<string, TypeExpression> GenericBindings);

    private sealed record ExtensionRegistration(MethodDeclaration Definition, NamespaceScope Scope);
    private readonly List<ExtensionRegistration> extensions = new();
    private readonly Dictionary<CallExpression, CallExpression> extensionCalls = new();
    private readonly Dictionary<CallExpression, MethodDeclaration> extensionDefinitions = new();
    private readonly Dictionary<CallExpression, MethodDeclaration> extensionTargets = new();
    private readonly HashSet<CallExpression> checkedExtensionArguments = new();
    private readonly HashSet<IdentifierExpression> extensionReceiverVariables = new();
    private readonly Dictionary<IdentifierExpression, TypeExpression> extensionReceiverSlotTypes = new();
    private readonly Dictionary<string, NamedTypeExpression> extensionGenericTypeOrigins = new();

    public CallExpression? GetExtensionCall(CallExpression call) => extensionCalls.GetValueOrDefault(call);
    public MethodDeclaration? GetExtensionDefinition(CallExpression call) => extensionDefinitions.GetValueOrDefault(call);
    public TypeExpression? GetExtensionReceiverStorageType(AstNode receiver) => receiver is IdentifierExpression id
        ? extensionReceiverSlotTypes.GetValueOrDefault(id) : _types.GetValueOrDefault(receiver);
    public bool CanBorrowExtensionReceiver(AstNode receiver)
    {
        if (!_types.ContainsKey(receiver) || IsPropertyValueStorage(receiver) || IsManagedStorage(receiver)) return false;
        if (IsReferenceRead(receiver)) return true;
        return receiver switch {
            IdentifierExpression id => extensionReceiverVariables.Contains(id) && !_constValues.ContainsKey(id),
            UnaryExpression { Operator: TokenKind.Star } u => GetType(u.Operand) is PointerTypeExpression,
            MemberAccessExpression m => GetType(m.Object) is PointerTypeExpression || CanBorrowExtensionReceiver(m.Object),
            IndexExpression i => GetType(i.Target) is PointerTypeExpression || CanBorrowExtensionReceiver(i.Target), _ => false };
    }
    public bool IsExtensionReceiverReadOnly(AstNode receiver) => _types.TryGetValue(receiver, out var type) &&
        (type.IsReadOnlyValue || _constValues.ContainsKey(receiver));

    private void RegisterExtensions(CompilationUnit unit)
    {
        extensions.Clear();
        Walk(unit.Members, _globalScope, true);
        foreach (var ns in unit.Namespaces) Walk(ns.Members, _namespaceScopes[ns], true);
        void Walk(IEnumerable<AstNode> members, NamespaceScope scope, bool namespaceMembers)
        {
            foreach (var member in members)
            {
                if (member is NamespaceDeclaration ns) Walk(ns.Members, _namespaceScopes[ns], true);
                else if (member is ClassDeclaration cls) Walk(cls.Members, scope, false);
                else if (member is StructDeclaration str) Walk(str.Members, scope, false);
                else if (member is InterfaceDeclaration iface) Walk(iface.Members, scope, false);
                else
                {
                    var parameters = member switch {
                        MethodDeclaration m => m.Parameters, ConstructorDeclaration c => c.Parameters,
                        ExternDeclaration e => e.Parameters, OperatorDeclaration o => o.Parameters, _ => null };
                    if (parameters == null || !parameters.Any(p => p.IsExtensionReceiver)) continue;
                    AnalyzeSafely(() => Register(member, parameters, scope, namespaceMembers));
                }
            }
        }
        void Register(AstNode member, List<Parameter> parameters, NamespaceScope scope, bool namespaceMembers)
        {
            using var context = SourceContext.Enter(member.Span);
            if (!namespaceMembers || member is not MethodDeclaration method || method.StringLiteralPrefix != null)
                throw new TypeCheckException("Extension receivers are allowed only on namespace functions", member.Span);
            if (!parameters[0].IsExtensionReceiver || parameters.Skip(1).Any(p => p.IsExtensionReceiver))
                throw new TypeCheckException("Only the first parameter can be an extension receiver", member.Span);
            if (parameters[0].DefaultValue != null || parameters[0].IsConst)
                throw new TypeCheckException("An extension receiver cannot have a default or be const", parameters[0].Span);
            extensions.Add(new(method, scope));
        }
    }

    private NamespaceScope? ExtensionNamespace(string path)
    {
        var scope = _globalScope;
        foreach (string component in path.Replace("$", "::").Split("::", StringSplitOptions.RemoveEmptyEntries))
        {
            if (!scope.Children.TryGetValue(component, out var next)) return null;
            scope = next;
        }
        return scope;
    }

    // Signature-only preview: no generic specialization, body checking, or symbol registration.
    public IReadOnlyList<ExtensionMethodCandidate> GetVisibleExtensions(string namespaceName,
        IEnumerable<string> usingNamespaces, TypeExpression receiverType, bool receiverIsStorage,
        bool receiverIsReadOnly, string? memberName = null, TypeExpression? receiverStorageType = null)
    {
        if (extensions.Count == 0) return [];
        var visible = new HashSet<NamespaceScope>();
        for (var scope = ExtensionNamespace(namespaceName); scope != null; scope = scope.Parent) visible.Add(scope);
        foreach (string name in usingNamespaces)
        {
            NamespaceScope? scope = ExtensionNamespace(name);
            if (scope == null)
                foreach (var parent in visible.ToArray())
                    if (parent.Children.TryGetValue(name, out scope)) break;
            if (scope != null) visible.Add(scope);
        }
        var result = new List<ExtensionMethodCandidate>();
        foreach (var entry in extensions.Where(e => visible.Contains(e.Scope) && (memberName == null || e.Definition.Name == memberName)))
        {
            TryPreview(entry.Definition, receiverType, receiverIsStorage, receiverIsReadOnly, ExtensionReceiverAdaptation.Exact);
            if (receiverType is PointerTypeExpression { IsNullable: false } pointer)
                TryPreview(entry.Definition, pointer.Inner, true, pointer.IsReadOnly || pointer.Inner.IsReadOnlyValue,
                    ExtensionReceiverAdaptation.RawPointerPointee);
        }
        return result;

        void TryPreview(MethodDeclaration method, TypeExpression source, bool storage, bool readOnly,
            ExtensionReceiverAdaptation adaptation)
        {
            var formal = ExtensionFormalType(method, method.Parameters[0].Type);
            bool reference = formal is ReferenceTypeExpression;
            if (reference && !storage) return;
            if (reference && adaptation == ExtensionReceiverAdaptation.Exact && receiverStorageType != null) source = receiverStorageType;
            if (readOnly) source = TypeQualifiers.ReadOnly(source);
            if (reference) source = new ReferenceTypeExpression(source, source.Line);
            var bindings = new Dictionary<string, TypeExpression>();
            if (method.IsGeneric)
            {
                InferExtensionBindings(formal, source, method.GenericParameters.Select(p => p.Name).ToHashSet(), bindings);
                // An unbound receiver cannot be offered honestly. Other parameters may infer remaining T's.
                var substituted = SubstituteExtensionType(formal, bindings);
                if (HasUnboundExtensionType(substituted, method.GenericParameters.Select(p => p.Name).ToHashSet())) return;
                formal = substituted;
                foreach (var parameter in method.GenericParameters)
                    if (parameter.Constraint != null && bindings.TryGetValue(parameter.Name, out var bound))
                    {
                        var constraint = SubstituteExtensionType(ExtensionFormalType(method, parameter.Constraint), bindings);
                        if (!HasUnboundExtensionType(constraint, method.GenericParameters.Select(p => p.Name).ToHashSet()) &&
                            !ExtensionConstraintSatisfied(bound, KnownExtensionType(constraint))) return;
                    }
            }
            formal = KnownExtensionType(formal);
            if (formal == null) return;
            if (IsAssignable(formal, source)) result.Add(new(method, adaptation, reference, bindings));
        }
    }

    private TypeExpression SubstituteExtensionType(TypeExpression type, IReadOnlyDictionary<string, TypeExpression> bindings)
        => new AstCloner(bindings.ToDictionary(p => p.Key, p => p.Value)).CloneType(type);

    private bool ExtensionConstraintSatisfied(TypeExpression argument, TypeExpression? constraint)
    {
        if (argument is PointerTypeExpression pointer) argument = pointer.Inner;
        if (argument is ManagedTypeExpression managed) argument = managed.Inner;
        if (argument is not NamedTypeExpression source || constraint is not NamedTypeExpression required) return false;
        if (_interfaces.ContainsKey(required.Name))
        {
            if (_structs.TryGetValue(source.Name, out var str)) return str.Interfaces.Contains(required.Name);
            return _classes.TryGetValue(source.Name, out var cls) && ClassImplementsInterface(cls, required.Name);
        }
        if (!_classes.ContainsKey(required.Name)) return false;
        for (var current = source.Name; current != null; current = _classes.GetValueOrDefault(current)?.BaseClass)
            if (current == required.Name) return true;
        return false;
    }

    private bool FunctionConstraintSatisfied(MethodDeclaration definition, TypeExpression argument, TypeExpression constraint)
    {
        // Type arguments have already been checked in the caller. Constraint
        // names and imports belong to the definition, like its specialized body.
        using var context = SourceContext.Enter(definition.Span);
        var previousNamespace = _currentNamespace;
        var previousPath = _currentNamespacePath;
        string path = GetFunctionNamespace(definition).Replace("$", "::");
        _currentNamespace = ExtensionNamespace(path) ?? _globalScope;
        _currentNamespacePath = path;
        try { return TypeSatisfiesConstraint(argument, constraint); }
        finally { _currentNamespace = previousNamespace; _currentNamespacePath = previousPath; }
    }

    private TypeExpression ExtensionFormalType(MethodDeclaration method, TypeExpression type)
    {
        var scope = extensions.First(e => ReferenceEquals(e.Definition, method)).Scope;
        var names = method.GenericParameters.Select(p => p.Name).ToHashSet();
        var expandingAliases = new HashSet<(NamespaceScope, string)>();
        return Qualify(type);
        TypeExpression Qualify(TypeExpression t)
        {
            TypeExpression result = t switch {
                ReferenceTypeExpression r => new ReferenceTypeExpression(Qualify(r.Inner), r.Line),
                PointerTypeExpression p => new PointerTypeExpression(Qualify(p.Inner), p.IsNullable, p.Line, p.IsReadOnly),
                ManagedTypeExpression m => new ManagedTypeExpression(Qualify(m.Inner), m.IsNullable, m.Line, m.IsReadOnly, m.Collector == null ? null : Qualify(m.Collector)) { UsesDefaultCollector = m.UsesDefaultCollector },
                ArrayTypeExpression a => new ArrayTypeExpression(Qualify(a.ElementType), a.Size, a.Line),
                FunctionPointerTypeExpression f => new FunctionPointerTypeExpression(Qualify(f.ReturnType),
                    f.ParameterTypes.Select(Qualify).ToList(), f.IsManaged, f.IsNullable, f.Line),
                NamedTypeExpression n => QualifyNamed(n), _ => t };
            return result.WithReadOnlyValue(t.IsReadOnlyValue || result.IsReadOnlyValue);
        }
        TypeExpression QualifyNamed(NamedTypeExpression n)
        {
            if (n.IsResolvedNominal || names.Contains(n.Name)) return n;
            string identity = n.Name;
            if (n.Namespace != null)
            {
                if (ExtensionNamespace(n.Namespace) is { } explicitScope)
                {
                    if (explicitScope.Aliases.TryGetValue(n.Name, out var target)) return ExpandAlias(explicitScope, n.Name, target);
                    if (explicitScope.TypeNames.TryGetValue(n.Name, out var qualified)) identity = qualified;
                }
            }
            else
            {
                for (var current = scope; current != null; current = current.Parent)
                {
                    if (current.Aliases.TryGetValue(n.Name, out var target)) return ExpandAlias(current, n.Name, target);
                    if (current.TypeNames.TryGetValue(n.Name, out var qualified)) { identity = qualified; break; }
                }
                if (identity == n.Name && method.Span.SourceId is { } id && _compilationUnit?.FileUsings.TryGetValue(id, out var imports) == true)
                    foreach (string import in imports)
                        if (ExtensionNamespace(import) is { } imported)
                        {
                            if (imported.Aliases.TryGetValue(n.Name, out var target)) return ExpandAlias(imported, n.Name, target);
                            if (imported.TypeNames.TryGetValue(n.Name, out var qualified))
                            {
                                if (identity != n.Name && identity != qualified) return n;
                                identity = qualified;
                            }
                        }
            }
            return MarkResolvedNominal(new NamedTypeExpression(identity, null, n.Line, n.TypeArguments.Select(Qualify).ToList()));
        }
        TypeExpression ExpandAlias(NamespaceScope aliasScope, string name, TypeExpression target)
        {
            if (!expandingAliases.Add((aliasScope, name))) return new NamedTypeExpression(name, null, target.Line);
            var previous = scope;
            scope = aliasScope;
            try { return Qualify(target); }
            finally { scope = previous; expandingAliases.Remove((aliasScope, name)); }
        }
    }

    private TypeExpression? KnownExtensionType(TypeExpression type)
    {
        // Never instantiate a type merely to offer a completion. An applicable
        // generic receiver's concrete layout must already have been checked.
        if (type is NamedTypeExpression { TypeArguments.Count: > 0 } named)
        {
            var known = extensionGenericTypeOrigins.FirstOrDefault(pair =>
                pair.Value.Name == named.Name &&
                pair.Value.TypeArguments.Count == named.TypeArguments.Count &&
                pair.Value.TypeArguments.Zip(named.TypeArguments).All(p => TypesMatchPublic(p.First, p.Second)));
            return known.Key == null ? null : NamedTypeExpression.Resolved(known.Key, type.Line).WithReadOnlyValue(type.IsReadOnlyValue);
        }
        if (type is ReferenceTypeExpression r)
            return KnownExtensionType(r.Inner) is { } inner ? new ReferenceTypeExpression(inner, r.Line).WithReadOnlyValue(r.IsReadOnlyValue) : null;
        if (type is PointerTypeExpression p)
            return KnownExtensionType(p.Inner) is { } inner ? new PointerTypeExpression(inner, p.IsNullable, p.Line, p.IsReadOnly).WithReadOnlyValue(p.IsReadOnlyValue) : null;
        if (type is ManagedTypeExpression m)
            return KnownExtensionType(m.Inner) is { } inner ? new ManagedTypeExpression(inner, m.IsNullable, m.Line, m.IsReadOnly, m.Collector) { UsesDefaultCollector = m.UsesDefaultCollector }.WithReadOnlyValue(m.IsReadOnlyValue) : null;
        if (type is ArrayTypeExpression a)
            return KnownExtensionType(a.ElementType) is { } inner ? new ArrayTypeExpression(inner, a.Size, a.Line).WithReadOnlyValue(a.IsReadOnlyValue) : null;
        if (type is FunctionPointerTypeExpression function)
        {
            var result = KnownExtensionType(function.ReturnType);
            var parameters = function.ParameterTypes.Select(KnownExtensionType).ToList();
            if (result == null || parameters.Any(p => p == null)) return null;
            return new FunctionPointerTypeExpression(result, parameters.Select(p => p!).ToList(), function.IsManaged,
                function.IsNullable, function.Line).WithReadOnlyValue(function.IsReadOnlyValue);
        }
        // Unresolved nested generic syntax cannot be inferred without specializing
        // its parent. Nongeneric signatures have already resolved these nodes.
        if (type is NestedTypeExpression) return resolvedSourceTypes.GetValueOrDefault(type);
        return type;
    }

    private void InferExtensionBindings(TypeExpression formal, TypeExpression source, HashSet<string> names,
        Dictionary<string, TypeExpression> bindings)
    {
        switch (formal, source)
        {
            case (ReferenceTypeExpression f, ReferenceTypeExpression s): InferExtensionBindings(f.Inner, s.Inner, names, bindings); return;
            case (PointerTypeExpression f, PointerTypeExpression s): InferExtensionBindings(f.Inner, s.Inner, names, bindings); return;
            case (ManagedTypeExpression f, ManagedTypeExpression s):
                InferExtensionBindings(f.Inner, s.Inner, names, bindings);
                InferExtensionBindings(ResolveCollectorType(f.Collector, f.Line), ResolveCollectorType(s.Collector, s.Line), names, bindings); return;
            case (ArrayTypeExpression f, ArrayTypeExpression s): InferExtensionBindings(f.ElementType, s.ElementType, names, bindings); return;
            case (NamedTypeExpression f, NamedTypeExpression s) when f.TypeArguments.Count > 0:
                var origin = extensionGenericTypeOrigins.GetValueOrDefault(s.Name) ?? s;
                if (origin.Name == f.Name && origin.TypeArguments.Count == f.TypeArguments.Count)
                    foreach (var pair in f.TypeArguments.Zip(origin.TypeArguments)) InferExtensionBindings(pair.First, pair.Second, names, bindings);
                return;
        }
        InferTypeParameter(formal, source, names, bindings);
    }

    private bool HasUnboundExtensionType(TypeExpression type, ISet<string> names) => type switch {
        NamedTypeExpression n => !n.IsResolvedNominal && (names.Contains(n.Name) || n.TypeArguments.Any(t => HasUnboundExtensionType(t, names))),
        ReferenceTypeExpression r => HasUnboundExtensionType(r.Inner, names),
        PointerTypeExpression p => HasUnboundExtensionType(p.Inner, names),
        ManagedTypeExpression p => HasUnboundExtensionType(p.Inner, names) || p.Collector != null && HasUnboundExtensionType(p.Collector, names),
        ArrayTypeExpression a => HasUnboundExtensionType(a.ElementType, names),
        FunctionPointerTypeExpression f => HasUnboundExtensionType(f.ReturnType, names) || f.ParameterTypes.Any(p => HasUnboundExtensionType(p, names)),
        NestedTypeExpression n => HasUnboundExtensionType(n.Parent, names) || n.TypeArguments.Any(t => HasUnboundExtensionType(t, names)),
        _ => false };

    private bool HasCallableMember(TypeExpression receiver, string name)
    {
        receiver = ResolveAlias(receiver);
        receiver = receiver switch { PointerTypeExpression p => p.Inner, ManagedTypeExpression p => p.Inner, _ => receiver };
        if (receiver is not NamedTypeExpression named) return false;
        bool Callable(TypeExpression? type) => type != null && ResolveAlias(type) is FunctionPointerTypeExpression;
        if (ResolveInterface(named) is { } iface && (iface.MethodsByName.ContainsKey(name) ||
            Callable(iface.MethodsByName.GetValueOrDefault("$get$" + name)?.ReturnType))) return true;
        if (_classes.TryGetValue(named.Name, out var cls))
        {
            if (cls.Methods.ContainsKey(name)) return true;
            if (Callable(cls.Fields.FirstOrDefault(f => f.Name == name).Type)) return true;
            if (Callable(cls.Methods.GetValueOrDefault("$get$" + name).Method?.ReturnType)) return true;
        }
        if (_structs.TryGetValue(named.Name, out var str))
        {
            if (str.Methods.ContainsKey(name)) return true;
            if (Callable(str.Fields.FirstOrDefault(f => f.Name == name).Type)) return true;
            if (Callable(str.Methods.GetValueOrDefault("$get$" + name)?.ReturnType)) return true;
        }
        return false;
    }

    private bool TryCheckExtensionCall(CallExpression call)
    {
        if (call.Callee is not MemberAccessExpression { IsArrow: false } member || extensions.Count == 0 ||
            !extensions.Any(e => e.Definition.Name == member.Member)) return false;
        if (ResolveNamespace(member.Object) != null || ResolveEnum(member.Object) != null) return false;
        if (member.Object is IdentifierExpression id && !TryLookupVariable(id.Name, out _) &&
            (GetClass(id.Name) != null || GetStruct(id.Name) != null || IsInterface(id.Name))) return false;
        member.Object.Accept(this);
        var receiverType = ResolveAlias(GetType(member.Object));
        if (HasCallableMember(receiverType, member.Member)) return false;
        var candidates = GetVisibleExtensions(_currentNamespacePath, CurrentUsings, receiverType,
            IsReferenceStorage(member.Object) && !IsPropertyValueStorage(member.Object) && !IsManagedStorage(member.Object),
            IsExpressionReadOnly(member.Object), member.Member, GetExtensionReceiverStorageType(member.Object)).ToList();
        if (candidates.Any(c => c.Adaptation == ExtensionReceiverAdaptation.Exact))
            candidates.RemoveAll(c => c.Adaptation != ExtensionReceiverAdaptation.Exact);
        if (candidates.Count == 0) return false;
        // Receiver shape is selected before checking later arguments. Mixing ref and value
        // receivers would have different evaluation effects, so diagnose rather than guess.
        if (candidates.Select(c => c.IsReferenceReceiver).Distinct().Count() > 1)
            throw new TypeCheckException($"Extension receiver for '{member.Member}' is ambiguous", member.Span);
        AstNode receiver = member.Object;
        if (candidates[0].Adaptation == ExtensionReceiverAdaptation.RawPointerPointee)
        {
            receiver = new UnaryExpression(receiver, TokenKind.Star, true, receiver.Line) { Span = receiver.Span };
            var pointee = ((PointerTypeExpression)receiverType).Inner;
            if (((PointerTypeExpression)receiverType).IsReadOnly) pointee = TypeQualifiers.ReadOnly(pointee);
            RecordType(receiver, pointee);
        }
        if (candidates[0].IsReferenceReceiver)
        {
            var storageType = GetExtensionReceiverStorageType(receiver) ?? GetType(receiver);
            if (IsExpressionReadOnly(receiver)) storageType = TypeQualifiers.ReadOnly(storageType);
            if (NullableLocal(receiver) is { } escaped) { nullableEscaped.Add(escaped); NullableWrite(escaped); }
            receiver = new UnaryExpression(receiver, TokenKind.Ref, true, receiver.Line) { Span = receiver.Span };
            RecordType(receiver, new ReferenceTypeExpression(storageType, receiver.Line));
        }
        else if (!IsFreshValue(receiver) && RequiresCopy(GetType(receiver))) InvalidateNullableEscapes();
        foreach (var argument in call.Arguments) CheckNullableArgument(argument);
        var lowered = new CallExpression(new IdentifierExpression(member.Member, member.Line) { Span = member.Span },
            new[] { receiver }.Concat(call.Arguments).ToList(), call.Line, new List<TypeExpression>(call.TypeArguments)) { Span = call.Span };
        MethodDeclaration selected;
        if (candidates.Any(c => c.Definition.IsGeneric))
        {
            if (candidates.Count != 1) throw new TypeCheckException($"Generic extension call '{member.Member}' is ambiguous", call.Span);
            selected = candidates[0].Definition;
            if (lowered.TypeArguments.Count == 0)
            {
                var inferred = candidates[0].GenericBindings.ToDictionary(p => p.Key, p => p.Value);
                var names = selected.GenericParameters.Select(p => p.Name).ToHashSet();
                for (int i = 1; i < Math.Min(lowered.Arguments.Count, selected.Parameters.Count); i++)
                    InferExtensionBindings(selected.Parameters[i].Type, GetType(lowered.Arguments[i]), names, inferred);
                lowered.TypeArguments.AddRange(selected.GenericParameters.Select(p => inferred.TryGetValue(p.Name, out var type) ? type :
                    throw new TypeCheckException($"Could not infer type parameter '{p.Name}' for extension '{selected.Name}'", call.Span)));
            }
        }
        else selected = SelectBestOverload(candidates.Select(c => c.Definition), m => m.Parameters, lowered.Arguments, member.Member, call.Line);
        extensionTargets[lowered] = selected;
        checkedExtensionArguments.Add(lowered);
        lowered.Accept(this);
        extensionCalls[call] = lowered;
        extensionDefinitions[call] = selected;
        RecordType(member, GetType(lowered));
        RecordType(call, GetType(lowered));
        return true;
    }
}
