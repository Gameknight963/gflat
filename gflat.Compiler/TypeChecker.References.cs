using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    public TypeExpression ParameterValueType(TypeExpression type)
        => ResolveAlias(type) is ReferenceTypeExpression reference ? reference.Inner : ResolveAlias(type);

    public void Visit(ReferenceTypeExpression node) => ValidateTypeUsage(node, node.Line);

    private void CheckReferenceArgument(UnaryExpression node)
    {
        using var context = SourceContext.Enter(node.Span);
        CheckNullableStorage(node.Operand, escape: true);
        if (!IsReferenceStorage(node.Operand) || IsPropertyValueStorage(node.Operand))
            throw new TypeCheckException("ref requires existing storage, not a temporary or value property", node.Span);
        if (IsManagedStorage(node.Operand))
            throw new TypeCheckException("Cannot borrow a reference into managed storage", node.Span);
        var type = GetType(node.Operand);
        if (IsExpressionImmutable(node.Operand)) type = TypeQualifiers.Immutable(type);
        else if (IsExpressionReadOnly(node.Operand)) type = TypeQualifiers.ReadOnly(type);
        RecordType(node, new ReferenceTypeExpression(type, node.Line));
    }

    private bool IsReferenceStorage(AstNode expression)
    {
        if (IsReferenceRead(expression)) return true;
        return expression switch
        {
            IdentifierExpression id => TryLookupVariable(id.Name, out _) && !_constValues.ContainsKey(id),
            UnaryExpression { Operator: TokenKind.Star } u => ResolveAlias(GetType(u.Operand)) is PointerTypeExpression,
            MemberAccessExpression m => ResolveAlias(GetType(m.Object)) is PointerTypeExpression || IsReferenceStorage(m.Object),
            IndexExpression i => ResolveAlias(GetType(i.Target)) is PointerTypeExpression || IsReferenceStorage(i.Target),
            _ => false
        };
    }
}
