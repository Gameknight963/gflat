using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private readonly Dictionary<NewExpression, MethodDeclaration> _managedAllocations = new();
    private bool _usesCollector;
    private bool HasCollectorRuntime => ResolveNamespaceByName("GC")?.Functions.ContainsKey("Allocate") == true;
    public bool UsesCollector => _usesCollector;
    private bool HasLegacyManagedAllocator => _globalScope.Functions.ContainsKey("__gflat_gc_alloc") || _globalScope.Externs.ContainsKey("__gflat_gc_alloc");
    public MethodDeclaration? GetManagedAllocation(NewExpression node) => _managedAllocations.GetValueOrDefault(node);
    public bool HasCollectorNativeTracking => UsesCollector && ResolveNamespaceByName("GC")?.Functions.ContainsKey("TrackNative") == true;

    private void ValidateCollectorHooks()
    {
        if (!HasCollectorRuntime) return;
        var scope = ResolveNamespaceByName("GC")!;
        MethodDeclaration allocate = scope.Functions["Allocate"];
        if (allocate.IsGeneric || allocate.Body == null || SignatureType(allocate.ReturnType, allocate) != "void*" ||
            !allocate.Parameters.Select(p => SignatureType(p.Type, allocate)).SequenceEqual(new[] { "nuint", "nuint" }))
            throw new TypeCheckException("GC::Allocate must have signature void* Allocate(nuint bytes, nuint alignment)", allocate.Span);
        var contracts = new Dictionary<string, (string Result, string[] Parameters)> {
            ["ReferenceAdded"] = ("void", ["void*"]),
            ["ReferenceRemoved"] = ("void", ["void*"])
        };
        foreach (var (name, contract) in contracts)
        {
            if (!scope.Functions.TryGetValue(name, out MethodDeclaration? hook))
                throw new TypeCheckException($"GC::Allocate requires the compiler-facing GC::{name} hook", allocate.Span);
            if (hook.IsGeneric || !hook.IsNoThrow || hook.Body == null ||
                SignatureType(hook.ReturnType, hook) != contract.Result ||
                !hook.Parameters.Select(p => SignatureType(p.Type, hook)).SequenceEqual(contract.Parameters))
                throw new TypeCheckException($"GC::{name} has an invalid runtime signature or is missing nothrow", hook.Span);
        }
    }

    private void CheckManagedAllocation(NewExpression node)
    {
        _usesCollector = true;
        _managedAllocations[node] = ResolveNamespaceByName("GC")!.Functions["Allocate"];
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
