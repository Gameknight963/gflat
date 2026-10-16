using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private void CheckPlacementDestination(NewExpression node, TypeExpression constructedType)
    {
        if (node.Destination == null) return;
        node.Destination.Accept(this);
        TypeExpression destination = ResolveAlias(GetType(node.Destination));
        if (node.Kind != AllocationKind.Pointer || constructedType.IsReadOnlyValue ||
            destination is not PointerTypeExpression { IsNullable: false, IsReadOnly: false } pointer ||
            !TypesMatch(constructedType, pointer.Inner))
            throw new TypeCheckException("Placement destination must be a non-null raw pointer to the exact mutable constructed type", node.Destination.Span);
    }
}
