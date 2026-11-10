using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private readonly HashSet<string> resolvingInterfaceRequirements = new();

    public bool InterfaceImplements(string source, string target)
    {
        if (source == target) return true;
        if (!_interfaces.TryGetValue(source, out var contract)) return false;
        ResolveInterfaceRequirements(contract);
        return contract.RequiredInterfaces.Contains(target);
    }

    private void ResolveInterfaceRequirements(InterfaceInfo contract)
    {
        if (contract.RequirementsResolved) return;
        var declaration = contract.Declaration!;
        if (declaration.RequiredInterfaces.Count == 0)
        {
            contract.RequirementsResolved = true;
            return;
        }
        if (!resolvingInterfaceRequirements.Add(contract.Name))
            throw new TypeCheckException($"Circular interface requirement involving '{DisplayName(contract.Name)}'", declaration.Span);
        try
        {
            using var context = EnterSignatureContext(declaration, null, false);
            var direct = new HashSet<string>();
            var requirements = new List<string>();
            foreach (var syntax in declaration.RequiredInterfaces)
            {
                using var source = SourceContext.Enter(syntax.Span);
                var resolved = ResolveAlias(syntax);
                if (resolved is not NamedTypeExpression named || GetInterface(named.Name) is not { } required)
                    throw new TypeCheckException("An interface requirement must name an interface", syntax.Span);
                if (!direct.Add(required.Name))
                    throw new TypeCheckException($"Duplicate interface requirement '{DisplayName(required.Name)}'", syntax.Span);
                if (contract.IsStaticContract != required.IsStaticContract)
                    throw new TypeCheckException("An interface requirement must match the interface's static contract", syntax.Span);
                ResolveInterfaceRequirements(required);
                foreach (var method in required.Methods)
                {
                    if (!contract.MethodsByName.TryGetValue(method.Name, out var own))
                        throw new TypeCheckException($"Interface '{DisplayName(contract.Name)}' must explicitly declare required member '{DisplayName(required.Name)}.{method.PropertyName ?? method.Name}'", declaration.Span);
                    BindMethodSignature(method);
                    BindMethodSignature(own);
                    bool matches = own.IsStatic == method.IsStatic &&
                        own.IsRefReturn == method.IsRefReturn &&
                        (!method.IsNoThrow || own.IsNoThrow) &&
                        (!method.IsReadOnly || own.IsReadOnly) &&
                        (!method.IsImmutable || own.IsImmutable) &&
                        own.GenericParameters.Count == method.GenericParameters.Count &&
                        TypesMatchPublic(ResolveHierarchyMethodType(own, own.ReturnType), ResolveHierarchyMethodType(method, method.ReturnType)) &&
                        own.Parameters.Count == method.Parameters.Count;
                    for (int index = 0; matches && index < method.Parameters.Count; index++)
                        matches = own.Parameters[index].IsConst == method.Parameters[index].IsConst &&
                            TypesMatchPublic(ResolveHierarchyMethodType(own, own.Parameters[index].Type),
                                ResolveHierarchyMethodType(method, method.Parameters[index].Type));
                    if (!matches)
                        throw new TypeCheckException($"Interface member '{DisplayName(contract.Name)}.{own.PropertyName ?? own.Name}' does not satisfy required member '{DisplayName(required.Name)}.{method.PropertyName ?? method.Name}'", own.Span);
                }
                foreach (string name in new[] { required.Name }.Concat(required.RequiredInterfaces))
                    if (!requirements.Contains(name)) requirements.Add(name);
            }
            contract.RequiredInterfaces = requirements;
            contract.RequirementsResolved = true;
        }
        finally { resolvingInterfaceRequirements.Remove(contract.Name); }
    }

    private List<string> ExpandInterfaceRequirements(List<string> interfaces)
    {
        var result = new List<string>(interfaces);
        foreach (string name in interfaces)
        {
            if (GetInterface(name) is not { } contract) continue;
            ResolveInterfaceRequirements(contract);
            foreach (string required in contract.RequiredInterfaces)
                if (!result.Contains(required)) result.Add(required);
        }
        return result;
    }
}
