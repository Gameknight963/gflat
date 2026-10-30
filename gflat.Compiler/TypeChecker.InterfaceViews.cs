using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    // A bare interface cast is an ephemeral receiver, not an interface value with storage.
    // The emitter borrows an existing address or uses ordinary full-expression temporary cleanup.
    public sealed record InterfaceViewCastInfo(TypeExpression SourceReference,
        PointerTypeExpression ViewReference, bool BorrowValueStorage);

    private readonly Dictionary<CastExpression, InterfaceViewCastInfo> interfaceViewCasts = new();
    private readonly HashSet<CastExpression> interfaceViewReceiverCasts = new();

    public bool TryGetInterfaceViewCast(CastExpression expression, out InterfaceViewCastInfo view)
        => interfaceViewCasts.TryGetValue(expression, out view!);

    // Only extension lowering may adapt the ephemeral view to its borrowed pointer
    // parameter. Ordinary interface-value assignment/conversion remains forbidden.
    private bool TryCreateInterfaceViewReceiver(AstNode source, out AstNode receiver)
    {
        receiver = source;
        if (source is not CastExpression cast || !interfaceViewCasts.TryGetValue(cast, out var view) ||
            ResolveAlias(GetType(source)) is not NamedTypeExpression) return false;
        var adapted = new CastExpression(view.ViewReference, source, source.Line) { Span = source.Span };
        interfaceViewReceiverCasts.Add(adapted);
        interfaceViewCasts[adapted] = new(view.ViewReference, view.ViewReference, false);
        RecordType(adapted, view.ViewReference);
        receiver = adapted;
        return true;
    }

    private bool TryCheckInterfaceViewCast(CastExpression node, TypeExpression sourceType, TypeExpression targetType)
    {
        if (interfaceViewReceiverCasts.Contains(node) && node.Operand is CastExpression original &&
            interfaceViewCasts.TryGetValue(original, out var originalView))
        {
            interfaceViewCasts[node] = new(originalView.ViewReference, originalView.ViewReference, false);
            RecordType(node, originalView.ViewReference);
            return true;
        }
        if (targetType is not NamedTypeExpression named || ResolveInterface(named) is not InterfaceInfo contract)
            return false;
        if (contract.IsStaticContract)
            throw new TypeCheckException($"Static interface '{DisplayName(contract.Name)}' cannot form an instance view", node.Span);

        TypeExpression sourceReference;
        bool borrowValueStorage;
        bool readOnly;
        bool immutable;
        if (node.Operand is CastExpression previous && interfaceViewCasts.TryGetValue(previous, out var previousView))
        {
            sourceReference = previousView.ViewReference;
            borrowValueStorage = false;
            readOnly = previousView.ViewReference.IsReadOnly;
            immutable = previousView.ViewReference.IsImmutable;
        }
        else if (sourceType is PointerTypeExpression pointer)
        {
            if (pointer.IsNullable)
                throw new TypeCheckException("An interface view requires a non-null receiver; check or assert the pointer first", node.Span);
            sourceReference = pointer;
            borrowValueStorage = false;
            readOnly = pointer.IsReadOnly;
            immutable = pointer.IsImmutable;
        }
        else if (sourceType is ManagedTypeExpression managed)
        {
            if (managed.IsNullable)
                throw new TypeCheckException("An interface view requires a non-null receiver; check or assert the reference first", node.Span);
            sourceReference = managed;
            borrowValueStorage = false;
            readOnly = managed.IsReadOnly;
            immutable = managed.IsImmutable;
        }
        else
        {
            ValidateValueType(sourceType, node.Line);
            ValidateArrayValueShape(sourceType, node.Line);
            readOnly = IsExpressionReadOnly(node.Operand);
            immutable = IsReceiverImmutable(node.Operand);
            TypeExpression borrowed = immutable ? TypeQualifiers.Immutable(sourceType)
                : readOnly ? TypeQualifiers.ReadOnly(sourceType) : sourceType;
            sourceReference = new PointerTypeExpression(borrowed, false, node.Line, isReadOnly: readOnly);
            borrowValueStorage = true;
        }

        TypeExpression viewType = targetType.WithReadOnlyValue(readOnly || targetType.IsReadOnlyValue)
            .WithImmutableValue(immutable || targetType.IsImmutableValue);
        var viewReference = new PointerTypeExpression(viewType, false, node.Line);
        TypeExpression checkedReference = sourceReference is ManagedTypeExpression managedSource
            ? new ManagedTypeExpression(viewType, false, node.Line, collector: managedSource.Collector)
                { UsesDefaultCollector = managedSource.UsesDefaultCollector }
            : viewReference;
        if (!IsAssignable(checkedReference, sourceReference))
            throw new TypeCheckException($"Type '{DisplayTypeName(sourceType)}' does not implement interface '{DisplayName(contract.Name)}'", node.Span);

        TypeExpression sourceValue = sourceReference is PointerTypeExpression sourcePointer
            ? ResolveAlias(sourcePointer.Inner) : ResolveAlias(((ManagedTypeExpression)sourceReference).Inner);
        // Reuse capability validation after ordinary pointer conformance has bound any
        // primitive implementation. A view must not reveal a hidden generic/alias capability.
        if (!TypesMatch(sourceValue, targetType) && !ExtensionConstraintSatisfied(sourceValue, targetType, strictDeclared: true))
            throw new TypeCheckException($"Interface '{DisplayName(contract.Name)}' is not exposed by the receiver's declared contract", node.Span);

        interfaceViewCasts[node] = new(sourceReference, viewReference, borrowValueStorage);
        RecordType(node, viewType);
        return true;
    }
}
