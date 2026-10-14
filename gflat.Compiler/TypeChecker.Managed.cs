using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private readonly Dictionary<NewExpression, MethodDeclaration> _managedAdoptions = new();
    private bool _usesCollector;
    private bool HasCollectorRuntime => ResolveNamespaceByName("GC")?.GenericFunctions.ContainsKey("Adopt") == true;
    public bool UsesCollector => _usesCollector;
    private bool HasLegacyManagedAllocator => _globalScope.Functions.ContainsKey("__gflat_gc_alloc") || _globalScope.Externs.ContainsKey("__gflat_gc_alloc");
    public MethodDeclaration? GetManagedAdoption(NewExpression node) => _managedAdoptions.GetValueOrDefault(node);

    private void ValidateCollectorHooks()
    {
        if (!HasCollectorRuntime) return;
        var scope = ResolveNamespaceByName("GC")!;
        MethodDeclaration adopt = scope.GenericFunctions["Adopt"];
        if (adopt.GenericParameters.Count != 1 || adopt.Parameters.Count != 1 || adopt.Body == null ||
            SignatureType(adopt.ReturnType, adopt) != "$type0^" || SignatureType(adopt.Parameters[0].Type, adopt) != "$type0*")
            throw new TypeCheckException("GC::Adopt must have signature T^ Adopt<T>(T* storage)", adopt.Span);
        var contracts = new Dictionary<string, (string Result, string[] Parameters)> {
            ["EnterFrame"] = ("void*", []),
            ["AddRoot"] = ("void", ["void*", "void*", "nuint"]),
            ["LeaveFrame"] = ("void", ["void*"]),
            ["TrackNative"] = ("bool", ["void*", "nuint"]),
            ["ForgetNative"] = ("void", ["void*"])
        };
        foreach (var (name, contract) in contracts)
        {
            if (!scope.Functions.TryGetValue(name, out MethodDeclaration? hook))
                throw new TypeCheckException($"GC::Adopt requires the compiler-facing GC::{name} hook", adopt.Span);
            if (hook.IsGeneric || !hook.IsNoThrow || hook.Body == null ||
                SignatureType(hook.ReturnType, hook) != contract.Result ||
                !hook.Parameters.Select(p => SignatureType(p.Type, hook)).SequenceEqual(contract.Parameters))
                throw new TypeCheckException($"GC::{name} has an invalid runtime signature or is missing nothrow", hook.Span);
        }
        if (!scope.Functions.TryGetValue("Collect", out MethodDeclaration? collect) || collect.IsGeneric ||
            collect.Body == null || collect.Parameters.Count != 0 || SignatureType(collect.ReturnType, collect) != "nuint")
            throw new TypeCheckException("GC::Adopt requires a concrete nuint GC::Collect() hook", adopt.Span);
    }

    private void CheckManagedAdoption(NewExpression node)
    {
        // Resolve the ordinary generic hook using a typed placeholder. The emitter
        // supplies the actual allocation; this expression is never executed.
        var raw = new PointerTypeExpression(node.Type, true, node.Line);
        var placeholder = new UnaryExpression(new DefaultExpression(raw, node.Line), TokenKind.Bang, false, node.Line);
        var call = new CallExpression(new NamespaceAccessExpression(new IdentifierExpression("GC", node.Line), "Adopt", node.Line),
            [placeholder], node.Line, [node.Type]);
        call.Accept(this);
        if (GetResolvedCall(call) is not MethodDeclaration method ||
            ResolveAlias(method.ReturnType) is not ManagedTypeExpression { IsNullable: false } result ||
            !TypesMatch(result.Inner, node.Type))
            throw new TypeCheckException("GC::Adopt<T> must return a non-null T^", node.Line);
        _managedAdoptions[node] = method;
        CheckAllocationFailure(node);
    }

    private bool ContainsManagedReference(TypeExpression type, HashSet<string>? visited = null)
    {
        type = ResolveAlias(type);
        if (type is ManagedTypeExpression) return true;
        if (type is PointerTypeExpression pointer) return ContainsManagedReference(pointer.Inner, visited);
        if (type is ReferenceTypeExpression reference) return ContainsManagedReference(reference.Inner, visited);
        if (type is ArrayTypeExpression array) return ContainsManagedReference(array.ElementType, visited);
        if (type is FunctionPointerTypeExpression function)
            return function.IsManaged || ContainsManagedReference(function.ReturnType, visited) ||
                function.ParameterTypes.Any(t => ContainsManagedReference(t, visited));
        if (type is not NamedTypeExpression named || IsPrimitive(named.Name)) return false;
        visited ??= new();
        if (!visited.Add(named.Name)) return false;
        if (GetStruct(named.Name) is { } structure)
            return structure.Fields.Any(f => ContainsManagedReference(f.Type, visited));
        if (GetClass(named.Name) is { } cls)
            return cls.Fields.Any(f => ContainsManagedReference(f.Type, visited));
        return false;
    }

    private bool IsManagedAdoptionBridge(ExternDeclaration node)
        => HasCollectorRuntime && node.Accessibility == TokenKind.Private && declarationNamespaces.GetValueOrDefault(node) == "GC" &&
            ResolveAlias(node.ReturnType) is ManagedTypeExpression { Inner: NamedTypeExpression { Name: "void" } } &&
            !node.Parameters.Any(p => ContainsManagedReference(p.Type));
}
