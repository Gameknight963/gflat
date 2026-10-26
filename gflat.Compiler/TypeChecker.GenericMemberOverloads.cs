using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private sealed record DeclaredMemberCandidate(MethodDeclaration Implementation, List<Parameter> Parameters);
    private readonly Dictionary<CallExpression, List<Parameter>> declaredMemberParameters = new();

    // Select through the receiver's declared contract, then call the matching
    // concrete implementation. Its extra overloads are not part of that contract.
    private MethodDeclaration? SelectDeclaredMemberOverload(MethodDeclaration method, CallExpression call)
    {
        TypeExpression? receiver;
        string memberName;
        bool staticAccess;
        if (call.Callee is MemberAccessExpression member)
        {
            receiver = _types.GetValueOrDefault(member.Object);
            memberName = member.Member;
            staticAccess = false;
        }
        else if (call.Callee is NamespaceAccessExpression access && StaticOwnerType(access.Left) is { } owner)
        {
            if (ResolveNamespace(access.Left) != null) return null;
            receiver = ResolveAlias(owner);
            memberName = access.Member;
            staticAccess = true;
        }
        else return null;
        if (receiver == null) return null;
        var constraints = DeclaredReceiverMemberConstraints(receiver, out bool aliasReceiver);
        if (constraints == null) return null;
        var implementations = overloadFamilies.GetValueOrDefault(method) ?? [method];
        // Unconstrained receivers retain the existing template-style member lookup.
        if (constraints.Count == 0)
            return implementations.Any(candidate => candidate.IsGeneric) ? null :
                SelectBestOverload(implementations, candidate => candidate.Parameters, call.Arguments,
                    memberName, call.Line, useDeclaredTypes: false);
        // Early specialization can inspect methods before their declaration pass.
        // Bind their signatures once in their own scope for selection and argument checking.
        foreach (var implementation in implementations.ToArray()) BindMethodSignature(implementation);
        var candidates = new List<DeclaredMemberCandidate>();
        var visited = new HashSet<string>();
        foreach (var constraint in EnumerateDeclaredReceiverMemberConstraints(constraints)) AddConstraint(constraint);
        if (candidates.Count == 0)
            throw new TypeCheckException($"No matching declared member '{memberName}'", call.Span);
        var selected = SelectBestOverload(candidates, candidate => candidate.Parameters, call.Arguments,
            memberName, call.Line, bindContextualArguments: false);
        declaredMemberParameters[call] = selected.Parameters;
        return selected.Implementation;

        void AddConstraint(TypeExpression constraint)
        {
            if (ResolveAlias(constraint) is not NamedTypeExpression named) return;
            var use = constraint.GenericUse ?? named.GenericUse ?? extensionGenericTypeOrigins.GetValueOrDefault(named.Name);
            AstNode? definition = use == null ? null :
                (AstNode?)_symbols.GenericInterfaces.GetValueOrDefault(use.Name) ??
                _genericClasses.GetValueOrDefault(use.Name) ?? (AstNode?)_genericStructs.GetValueOrDefault(use.Name);
            var genericParameters = definition switch
            {
                InterfaceDeclaration iface => iface.GenericParameters,
                ClassDeclaration cls => cls.GenericParameters,
                StructDeclaration structure => structure.GenericParameters,
                _ => null
            };
            var bindings = genericParameters == null || use == null ? new Dictionary<string, TypeExpression>() :
                genericParameters.Zip(use.TypeArguments).ToDictionary(pair => pair.First.Name, pair => pair.Second);
            foreach (var contract in DeclaredReceiverMemberMethods(constraint, memberName, staticAccess, aliasReceiver))
            {
                BindMethodSignature(contract);
                var parameters = contract.Parameters.Select(parameter =>
                {
                    var rebound = new Parameter(parameter.Name, Rebind(parameter.Type), parameter.Line,
                        parameter.IsConst, parameter.DefaultValue) { Span = parameter.Span };
                    if (defaultParameterContexts.TryGetValue(parameter, out var context)) defaultParameterContexts[rebound] = context;
                    if (parameterDefaults.TryGetValue(parameter, out var value)) parameterDefaults[rebound] = value;
                    return rebound;
                }).ToList();
                foreach (var implementation in implementations.Where(candidate => !candidate.IsGeneric &&
                    candidate.IsStatic == contract.IsStatic && candidate.Parameters.Count == parameters.Count &&
                    candidate.Parameters.Zip(parameters).All(pair => TypesMatch(
                        ClearIncomingGenericContracts(pair.First.Type), ClearIncomingGenericContracts(pair.Second.Type)))))
                {
                    string key = implementations.IndexOf(implementation) + ":" + string.Join(",", parameters.Select(parameter =>
                        CallableSymbolicTypeKey(OverloadArgumentType(parameter.Type), EmptyCallableTypeNames)));
                    if (visited.Add(key)) candidates.Add(new(implementation, parameters));
                }
            }

            TypeExpression Rebind(TypeExpression type)
            {
                if (type.GenericParameterView is { } parameter && ReferenceEquals(parameter.DeclaringDefinition, definition) &&
                    bindings.TryGetValue(parameter.ParameterName, out var argument)) return argument.WithQualifiersFrom(type);
                TypeExpression result = type switch
                {
                    PointerTypeExpression pointer => new PointerTypeExpression(Rebind(pointer.Inner), pointer.IsNullable, pointer.Line, pointer.IsReadOnly),
                    ManagedTypeExpression managed => new ManagedTypeExpression(Rebind(managed.Inner), managed.IsNullable, managed.Line,
                        managed.IsReadOnly, managed.Collector == null ? null : Rebind(managed.Collector)) { UsesDefaultCollector = managed.UsesDefaultCollector },
                    ReferenceTypeExpression reference => new ReferenceTypeExpression(Rebind(reference.Inner), reference.Line),
                    ArrayTypeExpression array => new ArrayTypeExpression(Rebind(array.ElementType), array.Size, array.Line, array.SizeExpression),
                    FunctionPointerTypeExpression function => new FunctionPointerTypeExpression(Rebind(function.ReturnType),
                        function.ParameterTypes.Select(Rebind).ToList(), function.IsManaged, function.IsNullable, function.Line, function.IsSignature),
                    NamedTypeExpression name when name.TypeArguments.Count > 0 => CopyNamed(name, name.TypeArguments.Select(Rebind).ToList()),
                    _ => type
                };
                if (type.GenericUse is { } genericUse) result = result.WithGenericUse(CopyNamed(genericUse, genericUse.TypeArguments.Select(Rebind).ToList()));
                return result.WithQualifiersFrom(type);
            }
        }

    }

    private IReadOnlyList<TypeExpression>? DeclaredReceiverMemberConstraints(TypeExpression receiver, out bool aliasReceiver)
    {
        IReadOnlyList<TypeExpression>? constraints = GetDeclaredParameterView(receiver)?.Constraints;
        aliasReceiver = false;
        if (aliasContracts.TryGetValue(receiver, out var alias) && boundAliasConstraints.TryGetValue(alias, out var exposedAlias))
        {
            constraints = exposedAlias;
            aliasReceiver = true;
        }
        return constraints;
    }

    private IEnumerable<TypeExpression> EnumerateDeclaredReceiverMemberConstraints(IReadOnlyList<TypeExpression> constraints)
    {
        var visited = new HashSet<string>();
        foreach (var constraint in constraints)
            foreach (var terminal in Walk(constraint)) yield return terminal;

        IEnumerable<TypeExpression> Walk(TypeExpression constraint)
        {
            if (constraint.GenericParameterView is { } inherited)
            {
                if (!visited.Add(inherited.OriginIdentity)) yield break;
                foreach (var parent in inherited.Constraints)
                    foreach (var terminal in Walk(parent)) yield return terminal;
            }
            else yield return constraint;
        }
    }

    private IEnumerable<MethodDeclaration> DeclaredReceiverMemberMethods(TypeExpression constraint, string name,
        bool? staticAccess, bool aliasReceiver)
    {
        if (ResolveAlias(constraint) is not NamedTypeExpression named) yield break;
        var contract = ResolveInterface(named);
        IEnumerable<MethodDeclaration> methods = contract?.Methods ??
            GetClassByIdentity(named.Name)?.Methods.Values.Select(entry => entry.Method) ?? [];
        foreach (var representative in methods.Where(method => method.Name == name && (staticAccess == null || method.IsStatic == staticAccess)))
        foreach (var method in overloadFamilies.GetValueOrDefault(representative) ?? [representative])
        {
            if (aliasReceiver && contract == null && method.Accessibility != TokenKind.Public) continue;
            yield return method;
        }
    }

    // A hidden implementation member must not change extension lookup through
    // a nonempty declared contract. An unconstrained template keeps duck lookup.
    private bool? HasDeclaredCallableMember(TypeExpression receiver, string name)
    {
        var constraints = DeclaredReceiverMemberConstraints(receiver, out bool aliasReceiver);
        if (constraints == null || constraints.Count == 0) return null;
        foreach (var constraint in EnumerateDeclaredReceiverMemberConstraints(constraints))
        {
            if (DeclaredReceiverMemberMethods(constraint, name, staticAccess: null, aliasReceiver).Any()) return true;
            foreach (var getter in DeclaredReceiverMemberMethods(constraint, "$get$" + name,
                staticAccess: null, aliasReceiver))
            {
                using var context = EnterMethodSignatureContext(getter);
                if (ResolveAlias(getter.ReturnType) is FunctionPointerTypeExpression) return true;
            }
            if (ResolveAlias(constraint) is not NamedTypeExpression named || GetClassByIdentity(named.Name) is not { } cls) continue;
            foreach (var field in cls.Fields.Where(field => field.Name == name &&
                (!aliasReceiver || field.Accessibility == TokenKind.Public)))
            {
                using var context = EnterFieldLayoutContext(field.DeclaringClass, field.Name);
                if (ResolveFieldLayoutType(field.Type) is FunctionPointerTypeExpression) return true;
            }
        }
        return false;
    }

}
