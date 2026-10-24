using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    public sealed record PrimitiveInterfaceImplementation(string Primitive, InterfaceInfo Interface,
        PrimitiveImplementationDeclaration Declaration, IReadOnlyDictionary<string, MethodDeclaration> Methods);
    private sealed record PrimitiveImplementationSource(InterfaceDeclaration Owner,
        PrimitiveImplementationDeclaration Declaration, Dictionary<string, MethodDeclaration> Helpers);
    private readonly List<PrimitiveImplementationSource> primitiveImplementationSources = new();
    private readonly Dictionary<(string Primitive, string Interface), PrimitiveInterfaceImplementation> primitiveImplementations = new();
    private readonly HashSet<PrimitiveImplementationSource> bindingPrimitiveImplementations = new();
    private readonly HashSet<PrimitiveImplementationSource> boundPrimitiveImplementations = new();
    private readonly Dictionary<MethodDeclaration, MethodDeclaration> primitiveMethodSources = new();
    private readonly HashSet<AstNode> checkedPrimitiveReceivers = new();
    private readonly Dictionary<CallExpression, TypeExpression> primitiveCallResults = new();
    private readonly Dictionary<CallExpression, Dictionary<string, TypeExpression>> primitiveCallArguments = new();
    public IEnumerable<PrimitiveInterfaceImplementation> GetPrimitiveImplementations() => primitiveImplementations.Values;
    public MethodDeclaration? GetPrimitiveMethodSource(MethodDeclaration method) => primitiveMethodSources.GetValueOrDefault(method);

    private void ExpandPrimitiveImplementations(CompilationUnit unit)
    {
        Walk(unit.Members);
        foreach (var ns in unit.Namespaces) Walk(ns.Members);
        void Walk(List<AstNode> members)
        {
            foreach (var node in members.ToArray())
            {
                if (node is NamespaceDeclaration ns) { Walk(ns.Members); continue; }
                if (node is ClassDeclaration or StructDeclaration)
                {
                    var children = node is ClassDeclaration c ? c.Members : ((StructDeclaration)node).Members;
                    foreach (var nested in children.OfType<InterfaceDeclaration>().Where(i => i.PrimitiveImplementations.Count > 0))
                        AnalyzeSafely(() => throw new TypeCheckException("Primitive implementations require a namespace-level interface", nested.Span));
                    continue;
                }
                if (node is not InterfaceDeclaration owner) continue;
                foreach (var declaration in owner.PrimitiveImplementations)
                    AnalyzeSafely(() => {
                    var helpers = new Dictionary<string, MethodDeclaration>();
                    foreach (var member in declaration.Members)
                    {
                        if (member is not MethodDeclaration source || source.Body == null || source.IsGeneric || source.IsStatic ||
                            source.IsVirtual || source.IsOverride || source.IsAbstract || source.IsConst || source.IsRefReturn ||
                            source.StringLiteralPrefix != null || source.ImplementationKind != FunctionImplementationKind.Ordinary)
                            throw new TypeCheckException("A primitive implementation can contain only ordinary instance methods with bodies", member.Span);
                        if (!helpers.TryAdd(source.Name, null!))
                            throw new TypeCheckException($"Duplicate primitive implementation method '{source.Name}'", source.Span);
                        var receiver = new PointerTypeExpression(declaration.Target, false, declaration.Line, isReadOnly: source.IsReadOnly);
                        var helper = new MethodDeclaration($"$primitive${owner.Name}${primitiveImplementationSources.Count}${source.Name}",
                            source.ReturnType, new[] { new Parameter("this", receiver, source.Line) }.Concat(source.Parameters).ToList(),
                            source.Body, TokenKind.Public, false, false, false, false, source.Line, isNoThrow: source.IsNoThrow)
                            { Span = source.Span, IsInline = source.IsInline, Attributes = source.Attributes };
                        helpers[source.Name] = helper;
                        primitiveMethodSources[helper] = source;
                        declarationNamespaces[source] = declarationNamespaces.GetValueOrDefault(owner) ?? "";
                        declarationNamespaces[helper] = declarationNamespaces.GetValueOrDefault(owner) ?? "";
                        namespaceHiddenDeclarations.Add(helper);
                    }
                    primitiveImplementationSources.Add(new(owner, declaration, helpers));
                    members.AddRange(helpers.Values);
                    });
            }
        }
    }

    private void RegisterPrimitiveImplementations()
    {
        foreach (var source in primitiveImplementationSources) AnalyzeSafely(() => BindPrimitiveImplementation(source));
    }

    private void BindPrimitiveImplementation(PrimitiveImplementationSource source)
    {
        if (boundPrimitiveImplementations.Contains(source)) return;
        if (!bindingPrimitiveImplementations.Add(source))
            throw new TypeCheckException("Cyclic primitive interface implementation", source.Declaration.Span);
        using var context = EnterSignatureContext(source.Owner, null, false);
        try
        {
            var target = ResolveAlias(source.Declaration.Target);
            if (target is not NamedTypeExpression primitive || !IsPrimitive(primitive.Name) || primitive.Name == "void" || target.IsReadOnlyValue)
                throw new TypeCheckException("An interface implementation target must be an unqualified primitive value type", source.Declaration.Target.Span);
            if (source.Owner.IsStatic || source.Owner.Members.OfType<MethodDeclaration>().Any(m => m.IsStatic))
                throw new TypeCheckException("Primitive implementations require an instance interface", source.Declaration.Span);
            TypeExpression contract;
            if (source.Declaration.Contract == null)
            {
                if (source.Owner.IsGeneric)
                    throw new TypeCheckException("An implementation in a generic interface must specify a closed interface specialization", source.Declaration.Span);
                contract = NamedTypeExpression.Resolved(source.Owner.Name, source.Declaration.Line);
            }
            else contract = ResolveAlias(source.Declaration.Contract);
            var iface = contract is NamedTypeExpression named ? GetInterface(named.Name) : null;
            string? definition = iface == null ? null : extensionGenericTypeOrigins.GetValueOrDefault(iface.Name)?.Name ?? iface.Name;
            if (iface == null || definition != source.Owner.Name)
                throw new TypeCheckException("A primitive implementation must implement its enclosing interface", source.Declaration.Span);
            var key = (primitive.Name, iface.Name);
            if (primitiveImplementations.ContainsKey(key))
                throw new TypeCheckException($"Duplicate implementation of '{DisplayName(iface.Name)}' for '{primitive.Name}'", source.Declaration.Span);
            foreach (var contractMethod in iface.Methods)
            {
                BindMethodSignature(contractMethod);
                if (!source.Helpers.TryGetValue(contractMethod.Name, out var helper))
                    throw new TypeCheckException($"Primitive '{primitive.Name}' does not implement interface method '{contractMethod.Name}'", source.Declaration.Span);
                var method = primitiveMethodSources[helper];
                BindMethodSignature(method);
                helper.ReturnType = method.ReturnType;
                helper.Parameters[0].Type = new PointerTypeExpression(primitive.WithInterfaceConstraints(new[] { contract }), false,
                    source.Declaration.Line, isReadOnly: method.IsReadOnly);
                if (contractMethod.IsNoThrow && !method.IsNoThrow || contractMethod.IsReadOnly && !method.IsReadOnly || contractMethod.IsImmutable && !method.IsImmutable ||
                    !TypesMatch(method.ReturnType, contractMethod.ReturnType) || method.Parameters.Count != contractMethod.Parameters.Count ||
                    !method.Parameters.Zip(contractMethod.Parameters).All(p => TypesMatch(p.First.Type, p.Second.Type) && p.First.IsConst == p.Second.IsConst))
                    throw new TypeCheckException($"Primitive method '{method.Name}' must match its interface signature, readonly and nothrow requirements", method.Span);
            }
            if (source.Helpers.Keys.Any(name => !iface.MethodsByName.ContainsKey(name)))
                throw new TypeCheckException("A primitive implementation can define only members of its interface", source.Declaration.Span);
            primitiveImplementations.Add(key, new(primitive.Name, iface, source.Declaration, source.Helpers));
            boundPrimitiveImplementations.Add(source);
        }
        finally { bindingPrimitiveImplementations.Remove(source); }
    }

    private PrimitiveInterfaceImplementation? PrimitiveImplementation(string primitive, string iface)
    {
        if (primitiveImplementations.TryGetValue((primitive, iface), out var found)) return found;
        string definition = extensionGenericTypeOrigins.GetValueOrDefault(iface)?.Name ?? iface;
        foreach (var source in primitiveImplementationSources.Where(s => s.Owner.Name == definition))
            BindPrimitiveImplementation(source);
        return primitiveImplementations.GetValueOrDefault((primitive, iface));
    }

    private Dictionary<string, TypeExpression> ConstrainedTypeArguments(AstNode definition, List<GenericParameter> parameters,
        List<TypeExpression> arguments)
    {
        var map = parameters.Zip(arguments).ToDictionary(p => p.First.Name,
            p => ClearIncomingGenericContracts(SetPrimitiveConstraints(p.Second, Array.Empty<TypeExpression>())));
        var contracts = parameters.ToDictionary(parameter => parameter.Name, _ => new List<TypeExpression>());
        for (int index = 0; index < parameters.Count; index++)
        {
            var parameter = parameters[index];
            var actual = map[parameter.Name];
            map[parameter.Name] = actual.WithGenericParameterView(new GenericParameterContractView(
                GenericParameterOrigin(definition, index), definition, parameter.Name, actual, contracts[parameter.Name]));
        }
        foreach (var parameter in parameters)
        {
            if (parameter.Constraint == null) continue;
            var argument = map[parameter.Name];
            using var context = EnterSignatureContext(definition, null, false);
            var constraint = ResolveAlias(new AstCloner(map).CloneType(parameter.Constraint));
            contracts[parameter.Name].Add(constraint);
            var leaf = argument switch { PointerTypeExpression p => p.Inner, ManagedTypeExpression m => m.Inner, _ => argument };
            if (leaf is not NamedTypeExpression primitive || !IsPrimitive(primitive.Name)) continue;
            if (constraint is NamedTypeExpression named && GetInterface(named.Name) is { IsStaticContract: false })
                map[parameter.Name] = SetPrimitiveConstraints(argument, new[] { constraint });
        }
        return map;
    }

    private void BindPrimitiveCallResult(CallExpression call, MethodDeclaration definition, List<TypeExpression> arguments)
    {
        using var context = EnterMethodSignatureContext(definition);
        var map = definition.GenericParameters.Zip(arguments).ToDictionary(p => p.First.Name, p => p.Second);
        primitiveCallArguments[call] = map;
        primitiveCallResults[call] = ResolveAlias(new AstCloner(map).CloneType(SetPrimitiveConstraints(definition.ReturnType, Array.Empty<TypeExpression>())));
    }

    private TypeExpression BindPrimitiveGenericUse(TypeExpression type, List<TypeExpression> arguments)
    {
        if (type is not NamedTypeExpression named ||
            !extensionGenericTypeOrigins.TryGetValue(named.Name, out var origin)) return type;
        return type.WithGenericUse(new NamedTypeExpression(origin.Name, null, type.Line, arguments));
    }

    private (AstNode Definition, List<AstNode> Members, Dictionary<string, TypeExpression> Bindings)? PrimitiveGenericMembers(
        TypeExpression receiver, string? declaringOwner = null)
    {
        receiver = ResolveAlias(receiver);
        receiver = receiver switch { PointerTypeExpression p => p.Inner, ManagedTypeExpression m => m.Inner,
            ReferenceTypeExpression r => r.Inner, _ => receiver };
        var visited = new HashSet<string>();
        while (receiver is NamedTypeExpression named && visited.Add(named.Name))
        {
            var use = named.GenericUse ?? extensionGenericTypeOrigins.GetValueOrDefault(named.Name);
            var cls = GetClassByIdentity(named.Name);
            AstNode? definition = use == null ? cls?.Declaration :
                (AstNode?)_genericStructs.GetValueOrDefault(use.Name) ?? _genericClasses.GetValueOrDefault(use.Name);
            var parameters = definition switch { StructDeclaration s => s.GenericParameters, ClassDeclaration c => c.GenericParameters, _ => null };
            var members = definition switch { StructDeclaration s => s.Members, ClassDeclaration c => c.Members, _ => null };
            if (definition == null || parameters == null || members == null ||
                parameters.Count != (use?.TypeArguments.Count ?? 0)) return null;
            var bindings = parameters.Zip(use?.TypeArguments ?? []).ToDictionary(pair => pair.First.Name, pair => pair.Second);
            if (declaringOwner == null || named.Name == declaringOwner)
                return parameters.Count == 0 ? null : (definition, members, bindings);
            if (cls?.BaseClass == null) return null;

            // The checked hierarchy identifies the actual base. Rebind that
            // original declaration edge to retain this use's argument provenance.
            List<TypeExpression> bases = definition switch { ClassDeclaration c => c.BaseTypes, _ => [] };
            string originalName = ((ClassDeclaration)definition).Name;
            TypeExpression? parent = null;
            using (EnterSignatureContext(definition, named.Name, true))
            {
                foreach (var syntax in bases)
                {
                    var projected = ResolveAlias(new AstCloner(bindings, originalName, named.Name).CloneType(syntax));
                    if (projected is NamedTypeExpression candidate && candidate.Name == cls.BaseClass)
                    { parent = projected; break; }
                }
            }
            if (parent == null) return null;
            receiver = parent;
        }
        return null;
    }

    private TypeExpression PrimitiveCallResult(CallExpression call, MethodDeclaration method)
    {
        if (call.Callee is MemberAccessExpression member && TryGetMethodOwner(method, out var declaringOwner, out _) &&
            PrimitiveGenericMembers(GetType(member.Object), declaringOwner) is { } generic &&
            generic.Members.OfType<MethodDeclaration>().FirstOrDefault(m => m.Span == method.Span && m.Name == method.Name) is { } definition)
        {
            if (primitiveCallArguments.TryGetValue(call, out var methodArguments))
                foreach (var pair in methodArguments) generic.Bindings[pair.Key] = pair.Value;
            return ProjectGenericMemberType(definition.ReturnType, generic.Definition, declaringOwner, generic.Bindings);
        }
        if (call.Callee is IdentifierExpression && TryGetMethodOwner(method, out var owner, out _) &&
            owner == (_currentClass?.Name ?? _currentStruct?.Name) && !primitiveCallResults.ContainsKey(call)) return method.ReturnType;
        return primitiveCallResults.GetValueOrDefault(call) ?? ClearIncomingGenericContracts(
            SetPrimitiveConstraints(method.ReturnType, Array.Empty<TypeExpression>()), preserveAliases: true);
    }

    private TypeExpression PrimitiveFieldType(MemberAccessExpression access, TypeExpression fallback)
    {
        var receiver = ResolveAlias(GetType(access.Object));
        receiver = receiver switch { PointerTypeExpression p => p.Inner, ManagedTypeExpression m => m.Inner, _ => receiver };
        string? owner = (receiver as NamedTypeExpression)?.Name;
        if (owner != null && GetClassByIdentity(owner) is { } cls && cls.FieldIndex(access.Member) is int index && index >= 0)
            owner = cls.Fields[index].DeclaringClass;
        if (PrimitiveGenericMembers(receiver, owner) is not { } generic ||
            generic.Members.OfType<FieldDeclaration>().FirstOrDefault(f => f.Name == access.Member) is not { } definition)
            return owner == (_currentClass?.Name ?? _currentStruct?.Name) ? fallback :
                ClearIncomingGenericContracts(fallback, preserveAliases: true);
        return ProjectGenericMemberType(definition.Type, generic.Definition, owner!, generic.Bindings);
    }

    private TypeExpression ProjectGenericMemberType(TypeExpression type, AstNode definition, string owner,
        Dictionary<string, TypeExpression> bindings)
    {
        string name = definition switch { StructDeclaration structure => structure.Name,
            ClassDeclaration cls => cls.Name, _ => owner };
        // Fields do not independently own namespace/import context. Resolve their
        // original syntax in the generic owner's declaration scope, including self.
        using var context = EnterSignatureContext(definition, owner, GetClassByIdentity(owner) != null);
        var projected = ResolveAlias(new AstCloner(bindings, name, owner).CloneType(type));
        if (projected is NamedTypeExpression named && named.Name == owner)
        {
            List<GenericParameter> parameters = definition switch { StructDeclaration structure => structure.GenericParameters,
                ClassDeclaration cls => cls.GenericParameters, _ => [] };
            projected = BindPrimitiveGenericUse(projected, parameters.Select(parameter => bindings[parameter.Name]).ToList());
        }
        return projected;
    }

    private TypeExpression SetPrimitiveConstraints(TypeExpression type, IReadOnlyList<TypeExpression> constraints)
    {
        TypeExpression result = type;
        if (type is NamedTypeExpression primitive && IsPrimitive(primitive.Name))
            result = type.InterfaceConstraints.Count == 0 && constraints.Count == 0 ? type : type.WithInterfaceConstraints(constraints);
        else if (type is PointerTypeExpression pointer)
        {
            var inner = SetPrimitiveConstraints(pointer.Inner, constraints);
            if (!ReferenceEquals(inner, pointer.Inner)) result = new PointerTypeExpression(inner, pointer.IsNullable, pointer.Line, pointer.IsReadOnly) { Span = pointer.Span };
        }
        else if (type is ManagedTypeExpression managed)
        {
            var inner = SetPrimitiveConstraints(managed.Inner, constraints);
            if (!ReferenceEquals(inner, managed.Inner)) result = new ManagedTypeExpression(inner, managed.IsNullable, managed.Line, managed.IsReadOnly, managed.Collector) { Span = managed.Span, UsesDefaultCollector = managed.UsesDefaultCollector };
        }
        if (constraints.Count == 0 && type.GenericUse is { } use)
        {
            var arguments = use.TypeArguments.Select(t => SetPrimitiveConstraints(t, constraints)).ToList();
            if (arguments.Zip(use.TypeArguments).Any(p => !ReferenceEquals(p.First, p.Second)))
                result = result.WithGenericUse(new NamedTypeExpression(use.Name, use.Namespace, use.Line, arguments));
        }
        if (!ReferenceEquals(result, type))
        {
            result = result.WithQualifiersFrom(type);
            if (type.GenericParameterView != null) result = result.WithGenericParameterView(type.GenericParameterView);
            if (aliasContracts.TryGetValue(type, out var contract)) aliasContracts[result] = contract;
        }
        return result;
    }

    private bool TryCheckPrimitiveInterfaceCall(CallExpression call)
    {
        if (primitiveImplementationSources.Count == 0 || call.Callee is not MemberAccessExpression { IsArrow: false } member) return false;
        if (ResolveNamespace(member.Object) != null || ResolveEnum(member.Object) != null) return false;
        member.Object.Accept(this);
        checkedPrimitiveReceivers.Add(member.Object);
        var receiver = ResolveAlias(GetType(member.Object));
        var valueType = receiver switch { PointerTypeExpression p => p.Inner, ManagedTypeExpression m => m.Inner, _ => receiver };
        valueType = ResolveAlias(valueType);
        if (valueType is not NamedTypeExpression primitive || !IsPrimitive(primitive.Name)) return false;
        var matches = valueType.InterfaceConstraints.OfType<NamedTypeExpression>()
            .Select(c => PrimitiveImplementation(primitive.Name, c.Name)).OfType<PrimitiveInterfaceImplementation>()
            .Where(i => i.Methods.ContainsKey(member.Member)).Distinct().ToArray();
        if (matches.Length == 0) return false;
        if (matches.Length > 1) throw new TypeCheckException($"Interface member '{member.Member}' is ambiguous", member.Span);
        var implementation = matches[0];
        var contract = implementation.Interface.MethodsByName[member.Member];
        if (call.TypeArguments.Count != 0) throw new TypeCheckException("Interface implementation methods are not generic", call.Span);
        if (IsNullable(receiver)) throw new TypeCheckException("Cannot call a method through a nullable pointer", member.Object.Span);
        if (IsExpressionReadOnly(member.Object) && !contract.IsReadOnly)
            throw new TypeCheckException($"Cannot call non-readonly method '{member.Member}' on readonly instance", member.Span);
        AstNode address = member.Object;
        if (receiver is not (PointerTypeExpression or ManagedTypeExpression))
        {
            address = new UnaryExpression(member.Object, TokenKind.Ampersand, true, member.Line) { Span = member.Object.Span };
            RecordType(address, new PointerTypeExpression(valueType, false, member.Line, isReadOnly: IsExpressionReadOnly(member.Object)));
        }
        foreach (var argument in call.Arguments) CheckNullableArgument(argument);
        AddDefaultArguments(call.Arguments, contract.Parameters, call);
        var helper = implementation.Methods[member.Member];
        var lowered = new CallExpression(new IdentifierExpression(helper.Name, call.Line),
            new[] { address }.Concat(call.Arguments).ToList(), call.Line) { Span = call.Span };
        extensionTargets[lowered] = helper;
        checkedExtensionArguments.Add(lowered);
        lowered.Accept(this);
        extensionCalls[call] = lowered;
        checkedPrimitiveReceivers.Remove(member.Object);
        RecordType(member, GetType(lowered));
        RecordType(call, GetType(lowered));
        return true;
    }
}
