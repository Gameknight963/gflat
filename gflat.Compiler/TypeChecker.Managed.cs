using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    public sealed record CollectorBinding(string Owner, bool IsClass, MethodDeclaration Allocate,
        MethodDeclaration ReferenceAdded, MethodDeclaration ReferenceRemoved);
    private readonly Dictionary<string, CollectorBinding> collectorBindings = new();
    private readonly Dictionary<NewExpression, MethodDeclaration> _managedAllocations = new();
    public bool UsesCollector => collectorBindings.Count != 0;
    public MethodDeclaration? GetManagedAllocation(NewExpression node) => _managedAllocations.GetValueOrDefault(node);

    private TypeExpression ResolveCollectorType(TypeExpression? collector, int line)
    {
        if (collector != null) return ResolveAlias(collector);
        if (_globalScope.Aliases.TryGetValue("GC", out var selected))
            return ResolveAliasBindingTarget(selected);
        // Type-only queries may describe the default without allocating storage.
        return new NamedTypeExpression("GC", null, line);
    }

    public CollectorBinding GetCollectorBinding(ManagedTypeExpression reference)
    {
        TypeExpression selected = ResolveCollectorType(reference.Collector, reference.Line);
        if (selected is not NamedTypeExpression named)
            throw new TypeCheckException("A managed collector must be a concrete provider type", reference.Span);
        if (collectorBindings.TryGetValue(named.Name, out var cached)) return cached;
        var cls = GetClass(named.Name);
        var structure = GetStruct(named.Name);
        if (cls == null && structure == null)
            throw new TypeCheckException($"Managed references require a collector provider; bind the global GC alias or use managed<T, Collector> (unknown '{DisplayTypeName(selected)}')", reference.Span);
        MethodDeclaration Require(string name, string result, string[] parameters, bool noThrow)
        {
            MethodDeclaration? method = cls != null ? cls.Methods.GetValueOrDefault(name).Method : structure!.Methods.GetValueOrDefault(name);
            if (method == null || !method.IsStatic || method.Accessibility != TokenKind.Public || method.IsGeneric ||
                SignatureType(method.ReturnType, method) != result || (noThrow && !method.IsNoThrow) ||
                !method.Parameters.Select(p => SignatureType(p.Type, method)).SequenceEqual(parameters))
                throw new TypeCheckException($"Collector '{DisplayTypeName(selected)}' requires public static {result} {name}({string.Join(", ", parameters)}){(noThrow ? " nothrow" : "")}", reference.Span);
            return method;
        }
        var allocation = Require("Allocate", "void*", ["nuint", "nuint"], false);
        var added = Require("ReferenceAdded", "void", ["void*"], true);
        var removed = Require("ReferenceRemoved", "void", ["void*"], true);
        return collectorBindings[named.Name] = new(named.Name, cls != null, allocation, added, removed);
    }

    private void CheckManagedAllocation(NewExpression node)
    {
        var reference = new ManagedTypeExpression(node.Type, false, node.Line, collector: node.Collector);
        _managedAllocations[node] = GetCollectorBinding(reference).Allocate;
    }

    private void ValidateManagedGraph(ManagedTypeExpression reference)
    {
        var visited = new HashSet<string>();
        TypeExpression collector = ResolveCollectorType(reference.Collector, reference.Line);
        Walk(reference.Inner);
        void Walk(TypeExpression type)
        {
            type = ResolveAlias(type);
            if (type is ManagedTypeExpression edge)
            {
                if (!TypesMatch(collector, ResolveCollectorType(edge.Collector, edge.Line)))
                    throw new TypeCheckException("Managed object graphs cannot contain references owned by a different collector", reference.Span);
                Walk(edge.Inner);
            }
            else if (type is ArrayTypeExpression array) Walk(array.ElementType);
            else if (type is NamedTypeExpression named && visited.Add(named.Name))
            {
                var fields = GetStruct(named.Name)?.Fields.Select(f => f.Type) ?? GetClass(named.Name)?.Fields.Select(f => f.Type);
                if (fields != null) foreach (var field in fields) Walk(field);
            }
        }
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

}
