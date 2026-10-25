using gflat.ast;

namespace gflat;

public partial class TypeChecker
{
    // A lookup view, not a declaration family: imports never alter symbol suffixes.
    private sealed record NamespaceCallableGroup(MethodDeclaration[] Methods, bool IsMerged);
    private readonly Dictionary<AstNode, MethodDeclaration[]> importedFunctionValueCandidates = new();
    private readonly Dictionary<CallExpression, IReadOnlyList<MethodDeclaration>> importedCallCandidates = new();

    public IReadOnlyList<MethodDeclaration> GetImportedCallCandidates(CallExpression call)
        => importedCallCandidates.GetValueOrDefault(call) ?? Array.Empty<MethodDeclaration>();

    private MethodDeclaration[] NamespaceMethods(NamespaceScope scope, string name)
    {
        var generic = scope.GenericFunctions.GetValueOrDefault(name);
        var ordinary = scope.Functions.GetValueOrDefault(name);
        if (generic == null && ordinary == null) return [];
        var methods = new List<MethodDeclaration>();
        void Add(MethodDeclaration? representative)
        {
            if (representative == null) return;
            foreach (var definition in overloadFamilies.TryGetValue(representative, out var family) ? family : [representative])
                if (!methods.Contains(definition)) methods.Add(definition);
        }
        Add(generic);
        Add(ordinary);
        return methods.ToArray();
    }

    private NamespaceCallableGroup ResolveNamespaceCallables(string name)
    {
        for (var scope = _currentNamespace; scope != null; scope = scope.Parent)
        {
            var methods = NamespaceMethods(scope, name);
            // A lexical callable shadows all outer/imported names even when inapplicable.
            if (methods.Length != 0 || scope.Externs.ContainsKey(name)) return new(methods, false);
        }
        var importedMethods = new List<MethodDeclaration>();
        var visited = new HashSet<NamespaceScope>();
        int contributors = 0;
        foreach (string imported in CurrentUsings)
        {
            if (ResolveNamespaceByName(imported, _currentNamespace) is not { } scope || !visited.Add(scope)) continue;
            var methods = NamespaceMethods(scope, name);
            if (methods.Length == 0) continue;
            contributors++;
            foreach (var method in methods) if (!importedMethods.Contains(method)) importedMethods.Add(method);
        }
        return new(importedMethods.ToArray(), contributors > 1);
    }

    private bool SameDeclarationFamily(MethodDeclaration[] methods)
    {
        if (methods.Length < 2) return true;
        return overloadFamilies.TryGetValue(methods[0], out var family) && methods.All(family.Contains);
    }

    private MethodDeclaration SelectImportedCallable(NamespaceCallableGroup group, CallExpression call, string name)
    {
        var eligible = group.Methods.Where(method => AcceptsArgumentCount(method.Parameters, call.Arguments.Count) &&
            (call.TypeArguments.Count == 0 || method.IsGeneric && method.GenericParameters.Count == call.TypeArguments.Count)).ToArray();
        // Wrong-arity imports must not change singleton/local-family template rules.
        if (eligible.Length != 0 && SameDeclarationFamily(eligible)) return SelectOverload(eligible[0], call);
        var selected = SelectCallableOverload(eligible.Length == 0 ? group.Methods : eligible, call, name);
        if (selected.Definition.IsGeneric) selectedGenericCallArguments[call] = selected.TypeArguments;
        else selectedGenericCallArguments.Remove(call);
        return selected.Definition;
    }

    private MethodDeclaration[] FunctionValueDefinitions(MethodDeclaration method, AstNode expression)
        => importedFunctionValueCandidates.TryGetValue(expression, out var imported) ? imported
            : overloadFamilies.TryGetValue(method, out var family) ? family.ToArray() : [method];
}
