using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private readonly HashSet<NewExpression> pendingCreations = new(ReferenceEqualityComparer.Instance);
    private static bool IsCreationPlaceholder(TypeExpression type)
        => type is NamedTypeExpression { Name: "<target-typed-new>" };
    private static bool IsUnboundCreation(AstNode node)
        => node is NewExpression { IsTargetTyped: true } creation && IsCreationPlaceholder(creation.Type);
    private static bool NeedsCreationTarget(AstNode node)
        => IsUnboundCreation(node) || node is ArrayLiteralExpression array && array.Elements.Any(NeedsCreationTarget);

    private void BindTargetTypedCreation(NewExpression creation, TypeExpression target)
    {
        target = ResolveAlias(target);
        TypeExpression constructed;
        switch (target)
        {
            case PointerTypeExpression pointer:
                creation.Kind = AllocationKind.Pointer;
                constructed = pointer.Inner;
                break;
            case ManagedTypeExpression managed:
                creation.Kind = AllocationKind.Managed;
                constructed = managed.Inner;
                if (creation.Collector != null && !TypesMatch(ResolveCollectorType(creation.Collector, creation.Line), ResolveCollectorType(managed.Collector, creation.Line)))
                    throw new TypeCheckException("The collector must match the target managed-reference type", creation.Span);
                creation.Collector = managed.UsesDefaultCollector ? null : managed.Collector;
                break;
            default:
                creation.Kind = AllocationKind.Value;
                constructed = target;
                break;
        }
        constructed = ResolveAlias(constructed).WithoutValueProtection();
        if (constructed is not NamedTypeExpression named || named.Name == "void" || ResolveInterface(named) != null)
            throw new TypeCheckException($"Target-typed creation requires a constructible value type, got '{DisplayTypeName(target)}'", creation.Span);
        if (creation.Destination != null && creation.Kind != AllocationKind.Pointer)
            throw new TypeCheckException("Placement construction requires a raw pointer target", creation.PlacementKeywordSpan);
        if (creation.Collector != null && creation.Kind != AllocationKind.Managed)
            throw new TypeCheckException("A collector can only be selected for a managed target", creation.Span);
        creation.Type = constructed;
        pendingCreations.Remove(creation);
        creation.Accept(this);
    }

    private void ValidateTargetTypedCreations()
    {
        if (_diagnostics.HasErrors) return;
        foreach (var creation in pendingCreations)
            AnalyzeSafely(() => throw new TypeCheckException("There is no target type for 'new(...)'", creation.Span));
    }
}
