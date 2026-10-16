using System.Text;
using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private readonly Dictionary<MethodDeclaration, List<MethodDeclaration>> overloadFamilies = new();

    public string GetOverloadSuffix(MethodDeclaration method) =>
        overloadFamilies.TryGetValue(method, out var family) && family.Count > 1
            ? "$overload$" + Convert.ToHexString(Encoding.UTF8.GetBytes(OverloadKey(method))) : "";

    public MethodDeclaration InterfaceImplementation(MethodDeclaration representative, MethodDeclaration contract)
    {
        if (!overloadFamilies.TryGetValue(representative, out var family)) return representative;
        string contractKey = OverloadKey(contract);
        return family.FirstOrDefault(m => OverloadKey(m) == contractKey) ?? representative;
    }

    // Readonly is not an overload discriminator, at any pointer depth.
    private string OverloadTypeKey(TypeExpression type)
    {
        type = ResolveAlias(type);
        return type switch
        {
            NamedTypeExpression n => n.Name,
            ReferenceTypeExpression r => "ref " + OverloadTypeKey(r.Inner),
            PointerTypeExpression p => OverloadTypeKey(p.Inner) + "*" + (p.IsNullable ? "?" : ""),
            ManagedTypeExpression p => OverloadTypeKey(p.Inner) + "^" + TypeName(ResolveCollectorType(p.Collector, p.Line)) + (p.IsNullable ? "?" : ""),
            ArrayTypeExpression a => OverloadTypeKey(a.ElementType) + $"[{a.Size}]",
            FunctionPointerTypeExpression f => OverloadTypeKey(f.ReturnType) + "(" + string.Join(",", f.ParameterTypes.Select(OverloadTypeKey)) + ")*" + (f.IsNullable ? "?" : ""),
            _ => TypeName(type)
        };
    }

    private string OverloadKey(List<Parameter> parameters) => string.Join(",", parameters.Select(p => OverloadTypeKey(p.Type)));

    private string OverloadKey(MethodDeclaration method)
    {
        using var context = EnterMethodSignatureContext(method);
        return OverloadKey(method.Parameters);
    }

    private void PrepareOverloads(CompilationUnit unit)
    {
        var groups = new Dictionary<string, List<MethodDeclaration>>();
        Walk(unit.Members, "");
        foreach (var ns in unit.Namespaces) Walk(ns.Members, ns.Name + "::");
        // Resolving a parameter alias can register another specialization.
        // Preserve this pass's owner set while nested passes prepare new types.
        var owners = _structs.Values.Select(s => (Owner: s.Name, IsClass: false, Constructors: s.Constructors.ToArray()))
            .Concat(_classes.Values.Select(c => (Owner: c.Name, IsClass: true, Constructors: c.Constructors.ToArray()))).ToArray();
        foreach (var owner in owners)
        {
            var keys = new Dictionary<string, ConstructorDeclaration>();
            foreach (var ctor in owner.Constructors)
            {
                using var context = EnterSignatureContext(ctor, owner.Owner, owner.IsClass);
                string key = OverloadKey(ctor.Parameters);
                if (keys.TryGetValue(key, out var first))
                    DuplicateDeclaration(first, ctor, "Constructors cannot differ only by readonly or parameter modifiers");
                keys[key] = ctor;
            }
        }
        foreach (var family in groups.Values.Where(g => g.Count > 1))
        {
            using var context = SourceContext.Enter(family[0].Span);
            if (family.Any(m => m.IsGeneric))
                throw new TypeCheckException("Overloaded generic function families are not yet supported", family[0].Line);
            if (family.Select(m => m.IsStatic).Distinct().Count() > 1)
                throw new TypeCheckException("An overload family cannot mix static and instance methods", family[0].Line);
            // Existing virtual tables use names as slots. Diagnose unsupported
            // families rather than selecting one overload by declaration order.
            if (family.Any(m => m.IsVirtual || m.IsOverride || m.IsAbstract || m.Body == null))
                throw new TypeCheckException("Overloaded virtual and interface methods are not yet supported", family[0].Line);
            var keys = new Dictionary<string, MethodDeclaration>();
            foreach (var method in family)
            {
                string key = OverloadKey(method);
                if (keys.TryGetValue(key, out var first))
                    DuplicateDeclaration(first, method, $"Overloads of '{method.Name}' cannot differ only by readonly, return type, or declaration modifiers");
                keys[key] = method;
                overloadFamilies[method] = family;
            }
        }
        void Walk(List<AstNode> members, string scope)
        {
            foreach (var member in members)
            {
                switch (member)
                {
                    case NamespaceDeclaration ns: Walk(ns.Members, scope + ns.Name + "::"); break;
                    case ClassDeclaration c when !c.IsGeneric: Walk(c.Members, scope + c.Name + "."); break;
                    case StructDeclaration s when !s.IsGeneric: Walk(s.Members, scope + s.Name + "."); break;
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
        if (!overloadFamilies.TryGetValue(method, out var family)) return method;
        return SelectBestOverload(family, m => m.Parameters, call.Arguments, method.Name, call.Line);
    }

    private T SelectBestOverload<T>(IEnumerable<T> candidates, Func<T, List<Parameter>> parameters,
        List<AstNode> arguments, string name, int line)
    {
        foreach (var arg in arguments) if (!_types.ContainsKey(arg)) arg.Accept(this);
        TypeExpression ArgumentType(AstNode arg) => arg is DefaultExpression { TargetType: null }
            ? new NamedTypeExpression("default", null, arg.Line) : GetType(arg);
        bool ArgumentMatches(TypeExpression expected, AstNode argument) => IsOverloadedFunctionValue(argument)
            ? CanSelectFunctionValue(argument, expected)
            : IsAssignable(expected, ArgumentType(argument), argument is DefaultExpression { TargetType: null } ? null : argument);
        var viable = candidates.Where(c => AcceptsArgumentCount(parameters(c), arguments.Count) &&
            parameters(c).Take(arguments.Count).Select((p, i) => ArgumentMatches(p.Type, arguments[i])).All(v => v)).ToList();
        bool Better(T first, T second)
        {
            bool strictly = false;
            for (int i = 0; i < arguments.Count; i++)
            {
                var a = parameters(first)[i].Type;
                var b = parameters(second)[i].Type;
                // A method group has no independently selected function type.
                // Both viable target signatures are equally good conversions;
                // ranking must not use a declaration-order representative.
                if (IsOverloadedFunctionValue(arguments[i])) continue;
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
            return strictly || parameters(first).Count == arguments.Count && parameters(second).Count > arguments.Count;
        }
        var best = viable.Where(c => !viable.Any(other => !ReferenceEquals(c, other) && Better(other, c))).ToList();
        if (best.Count == 0) throw new TypeCheckException($"No matching overload for '{name}'", line);
        if (best.Count > 1) throw new TypeCheckException($"Call to '{name}' is ambiguous", line);
        for (int i = 0; i < arguments.Count; i++)
            if (IsOverloadedFunctionValue(arguments[i]))
                IsAssignable(parameters(best[0])[i].Type, ArgumentType(arguments[i]), arguments[i]);
        return best[0];
    }
}
