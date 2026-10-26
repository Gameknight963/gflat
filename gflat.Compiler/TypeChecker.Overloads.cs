using System.Text;
using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private readonly Dictionary<MethodDeclaration, List<MethodDeclaration>> overloadFamilies = new();
    private readonly Dictionary<CallExpression, List<TypeExpression>> selectedGenericCallArguments = new();

    public string GetOverloadSuffix(MethodDeclaration method) =>
        overloadFamilies.TryGetValue(method, out var family) && family.Count > 1
            ? "$overload$" + Convert.ToHexString(Encoding.UTF8.GetBytes(method.IsGeneric ? GenericOverloadKey(method) : OverloadKey(method))) : "";

    public MethodDeclaration InterfaceImplementation(MethodDeclaration representative, MethodDeclaration contract)
    {
        if (!overloadFamilies.TryGetValue(representative, out var family)) return representative;
        string contractKey = OverloadKey(contract, useDeclaredTypes: false);
        return family.FirstOrDefault(m => OverloadKey(m, useDeclaredTypes: false) == contractKey) ?? representative;
    }

    // Readonly and immutable are not overload discriminators, at any pointer depth.
    private string OverloadTypeKey(TypeExpression type, bool useDeclaredTypes = true)
    {
        type = ResolveAlias(type);
        if (useDeclaredTypes) type = OverloadArgumentType(type);
        string Child(TypeExpression inner) => OverloadTypeKey(inner, useDeclaredTypes);
        return type switch
        {
            NamedTypeExpression n => n.Name,
            ReferenceTypeExpression r => "ref " + Child(r.Inner),
            PointerTypeExpression p => Child(p.Inner) + "*" + (p.IsNullable ? "?" : ""),
            ManagedTypeExpression p => Child(p.Inner) + "^" + Child(ResolveCollectorType(p.Collector, p.Line)) + (p.IsNullable ? "?" : ""),
            ArrayTypeExpression a => Child(a.ElementType) + $"[{a.Size}]",
            FunctionPointerTypeExpression f => Child(f.ReturnType) + "(" + string.Join(",", f.ParameterTypes.Select(Child)) + ")*" + (f.IsNullable ? "?" : ""),
            _ => TypeName(type)
        };
    }

    private string OverloadKey(List<Parameter> parameters, bool useDeclaredTypes = true) => string.Join(",", parameters.Select(p => OverloadTypeKey(p.Type, useDeclaredTypes)));

    private string OverloadKey(MethodDeclaration method, bool useDeclaredTypes = true)
    {
        // Reserved primitive names cannot depend on a declaration's aliases or
        // owner. Most already-bound overloads need no temporary lookup scope.
        if (method.Parameters.All(p => p.Type is NamedTypeExpression { Namespace: null, TypeArguments.Count: 0 } named && IsPrimitive(named.Name)))
            return OverloadKey(method.Parameters, useDeclaredTypes);
        using var context = EnterMethodSignatureContext(method);
        return OverloadKey(method.Parameters, useDeclaredTypes);
    }

    private void PrepareOverloads(CompilationUnit unit, AstNode? specialization = null, bool resolveSignatures = true)
    {
        var groups = new Dictionary<string, List<MethodDeclaration>>();
        var ownerNames = new HashSet<string>();
        if (specialization != null) Walk([specialization], "");
        else
        {
            Walk(unit.Members, "");
            foreach (var ns in unit.Namespaces) Walk(ns.Members, ns.Name + "::");
        }
        var families = groups.Values.Where(g => g.Count > 1).ToArray();
        foreach (var family in families)
        {
            using var context = SourceContext.Enter(family[0].Span);
            if (family.Select(m => m.IsStatic).Distinct().Count() > 1)
                throw new TypeCheckException("An overload family cannot mix static and instance methods", family[0].Line);
            // Existing virtual tables use names as slots.
            if (family.Any(m => m.IsVirtual || m.IsOverride || m.IsAbstract || m.Body == null))
                throw new TypeCheckException("Overloaded virtual and interface methods are not yet supported", family[0].Line);
            foreach (var method in family) overloadFamilies[method] = family;
        }
        // Generic bodies can be checked while declaration signatures bind. They
        // need all ordinary call candidates before semantic key validation starts.
        if (!resolveSignatures) return;
        // A nested specialization prepares only its own declarations. Revisiting
        // unrelated signatures here can reenter a type whose bases are resolving.
        var owners = _structs.Values.Where(s => ownerNames.Contains(s.Name))
            .Select(s => (Owner: s.Name, IsClass: false, Constructors: s.Constructors.ToArray()))
            .Concat(_classes.Values.Where(c => ownerNames.Contains(c.Name))
                .Select(c => (Owner: c.Name, IsClass: true, Constructors: c.Constructors.ToArray()))).ToArray();
        foreach (var owner in owners)
        {
            var keys = new Dictionary<string, ConstructorDeclaration>();
            foreach (var ctor in owner.Constructors)
            {
                using var context = EnterSignatureContext(ctor, owner.Owner, owner.IsClass);
                AnalyzeSafely(() =>
                {
                    string key = OverloadKey(ctor.Parameters);
                    if (keys.TryGetValue(key, out var first))
                        DuplicateDeclaration(first, ctor, "Constructors cannot differ only by readonly, immutable, or parameter modifiers");
                    keys[key] = ctor;
                });
            }
        }
        foreach (var family in families)
        {
            using var context = SourceContext.Enter(family[0].Span);
            var keys = new Dictionary<string, MethodDeclaration>();
            foreach (var method in family)
            {
                AnalyzeSafely(() =>
                {
                    string key = family.Any(m => m.IsGeneric) ? GenericOverloadKey(method) : OverloadKey(method);
                    if (keys.TryGetValue(key, out var first))
                        DuplicateDeclaration(first, method, $"Overloads of '{method.Name}' cannot differ only by readonly, immutable, return type, or declaration modifiers");
                    keys[key] = method;
                });
            }
        }
        void Walk(List<AstNode> members, string scope)
        {
            foreach (var member in members)
            {
                switch (member)
                {
                    case NamespaceDeclaration ns: Walk(ns.Members, scope + ns.Name + "::"); break;
                    case ClassDeclaration c when !c.IsGeneric:
                        ownerNames.Add(c.Name); Walk(c.Members, scope + c.Name + "."); break;
                    case StructDeclaration s when !s.IsGeneric:
                        ownerNames.Add(s.Name); Walk(s.Members, scope + s.Name + "."); break;
                    case InterfaceDeclaration i: Walk(i.Members, scope + i.Name + "."); break;
                    case MethodDeclaration m when m.StringLiteralPrefix == null:
                        string key = scope + m.Name;
                        if (!groups.TryGetValue(key, out var group)) groups[key] = group = new();
                        group.Add(m);
                        break;
                }
            }
        }
    }

    private MethodDeclaration SelectOverload(MethodDeclaration method, CallExpression call)
    {
        if (SelectDeclaredMemberOverload(method, call) is { } declaredMember) return declaredMember;
        overloadFamilies.TryGetValue(method, out var family);
        foreach (var candidate in (family ?? [method]).ToArray()) BindMethodSignature(candidate);
        if (method.IsGeneric || family?.Any(candidate => candidate.IsGeneric) == true)
        {
            var selected = SelectCallableOverload(family ?? [method], call, method.Name);
            if (selected.Definition.IsGeneric) selectedGenericCallArguments[call] = selected.TypeArguments;
            else selectedGenericCallArguments.Remove(call);
            return selected.Definition;
        }
        if (family == null) return method;
        var candidates = family.Select(definition => new CallableOverloadCandidate(definition, [], definition.Parameters.Select(parameter =>
            new Parameter(parameter.Name, BindCallableOwnerType(definition, call.Callee, parameter.Type), parameter.Line,
                parameter.IsConst, parameter.DefaultValue)).ToList())).ToList();
        return SelectBestOverload(candidates, candidate => candidate.Parameters, call.Arguments, method.Name, call.Line,
            useDeclaredTypes: !primitiveInterpolationCalls.Contains(call) && !HasUnconstrainedGenericReceiver(call)).Definition;
    }

    private bool HasUnconstrainedGenericReceiver(CallExpression call)
    {
        if (call.Callee is not MemberAccessExpression member || !_types.TryGetValue(member.Object, out var type)) return false;
        while (type.GenericParameterView == null)
        {
            type = type switch { PointerTypeExpression p => p.Inner, ManagedTypeExpression p => p.Inner,
                ReferenceTypeExpression r => r.Inner, _ => null };
            if (type == null) return false;
        }
        return type.GenericParameterView.Constraints.Count == 0;
    }

    private T SelectBestOverload<T>(IEnumerable<T> candidates, Func<T, List<Parameter>> parameters,
        List<AstNode> arguments, string name, int line, Func<T, T, bool>? preferWhenTied = null,
        bool bindContextualArguments = true, bool useDeclaredTypes = true)
    {
        foreach (var arg in arguments) if (!_types.ContainsKey(arg)) arg.Accept(this);
        TypeExpression ArgumentType(AstNode arg) => arg is DefaultExpression { TargetType: null }
            ? new NamedTypeExpression("default", null, arg.Line) : useDeclaredTypes ? OverloadArgumentType(GetType(arg)) : GetType(arg);
        TypeExpression ParameterType(TypeExpression type) => useDeclaredTypes ? OverloadArgumentType(type) : type;
        bool ArgumentMatches(TypeExpression expected, AstNode argument) => NeedsCreationTarget(argument) || (IsOverloadedFunctionValue(argument)
            ? CanSelectFunctionValue(argument, expected)
            : IsAssignable(ParameterType(expected), ArgumentType(argument),
                argument is DefaultExpression { TargetType: null } || _functionAddressTargets.ContainsKey(argument)
                    ? null : argument));
        var viable = candidates.Where(c => AcceptsArgumentCount(parameters(c), arguments.Count) &&
            parameters(c).Take(arguments.Count).Select((p, i) => ArgumentMatches(p.Type, arguments[i])).All(v => v)).ToList();
        bool Better(T first, T second)
        {
            bool strictly = false;
            for (int i = 0; i < arguments.Count; i++)
            {
                var a = ParameterType(parameters(first)[i].Type);
                var b = ParameterType(parameters(second)[i].Type);
                // A method group has no independently selected function type.
                // Both viable target signatures are equally good conversions;
                // ranking must not use a declaration-order representative.
                if (IsOverloadedFunctionValue(arguments[i]) || NeedsCreationTarget(arguments[i])) continue;
                var source = ArgumentType(arguments[i]);
                bool exactA = TypesMatch(a, source), exactB = TypesMatch(b, source);
                if (exactA != exactB)
                {
                    if (!exactA) return false;
                    strictly = true;
                }
                else if (!TypesMatch(a, b))
                {
                    bool aToB = IsAssignable(b, a), bToA = IsAssignable(a, b);
                    if (!aToB || bToA) return false;
                    strictly = true;
                }
            }
            // When supplied arguments have identical types, prefer the overload
            // requiring no defaults. Other ties stay ambiguous.
            if (strictly) return true;
            bool firstNeedsNoDefaults = parameters(first).Count == arguments.Count;
            bool secondNeedsNoDefaults = parameters(second).Count == arguments.Count;
            if (firstNeedsNoDefaults != secondNeedsNoDefaults) return firstNeedsNoDefaults;
            return preferWhenTied?.Invoke(first, second) == true;
        }
        var best = viable.Where(c => !viable.Any(other => !ReferenceEquals(c, other) && Better(other, c))).ToList();
        if (best.Count == 0) throw new TypeCheckException($"No matching overload for '{name}'", line);
        if (best.Count > 1) throw new TypeCheckException($"Call to '{name}' is ambiguous", line);
        for (int i = 0; i < arguments.Count; i++)
            if (bindContextualArguments && (IsOverloadedFunctionValue(arguments[i]) || NeedsCreationTarget(arguments[i])))
                IsAssignable(parameters(best[0])[i].Type, ArgumentType(arguments[i]), arguments[i]);
        return best[0];
    }
}
