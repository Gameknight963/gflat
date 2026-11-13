using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    public void Visit(ConditionalExpression node)
    {
        using var context = SourceContext.Enter(node.Span);
        node.Condition.Accept(this);
        if (!TypesMatch(GetType(node.Condition), Bool))
            throw new TypeCheckException("Conditional expression requires a bool condition", node.Condition.Span);
        var facts = NullableTruth(node.Condition);
        nullableFacts = new(facts.True);
        node.WhenTrue.Accept(this);
        var trueFacts = new HashSet<NullableBinding>(nullableFacts);
        nullableFacts = new(facts.False);
        node.WhenFalse.Accept(this);
        trueFacts.IntersectWith(nullableFacts);
        nullableFacts = trueFacts;

        TypeExpression left = ResolveAlias(GetType(node.WhenTrue)).WithoutValueProtection();
        TypeExpression right = ResolveAlias(GetType(node.WhenFalse)).WithoutValueProtection();
        if (left is NamedTypeExpression { Name: "null" }) left = NullableConditionalType(right);
        if (right is NamedTypeExpression { Name: "null" }) right = NullableConditionalType(left);
        if (IsCreationPlaceholder(left) && IsCreationPlaceholder(right))
        {
            RecordType(node, left);
            return;
        }
        TypeExpression target = IsCreationPlaceholder(left) ? right : left;
        if (!IsCreationPlaceholder(right) && !IsCreationPlaceholder(left) && !IsAssignable(target, right, node.WhenFalse))
        {
            if (!IsAssignable(right, left, node.WhenTrue))
                throw new TypeCheckException($"Conditional branches have incompatible types '{DisplayTypeName(left)}' and '{DisplayTypeName(right)}'", node.Span);
            target = right;
        }
        BindConditionalResult(node, target);
    }

    private static TypeExpression NullableConditionalType(TypeExpression type) => type switch
    {
        PointerTypeExpression p => new PointerTypeExpression(p.Inner, true, p.Line, p.IsReadOnly).WithQualifiersFrom(p),
        FunctionPointerTypeExpression p => new FunctionPointerTypeExpression(p.ReturnType, p.ParameterTypes, p.IsManaged, true, p.Line).WithQualifiersFrom(p),
        ManagedTypeExpression p => new ManagedTypeExpression(p.Inner, true, p.Line, p.IsReadOnly, p.Collector) { UsesDefaultCollector = p.UsesDefaultCollector }.WithQualifiersFrom(p),
        _ => type
    };

    private void BindConditionalResult(ConditionalExpression node, TypeExpression target)
    {
        foreach (AstNode branch in new[] { node.WhenTrue, node.WhenFalse })
            if (!IsAssignable(target, GetType(branch), branch))
                throw new TypeCheckException($"Conditional branch cannot produce '{DisplayTypeName(target)}'", branch.Span);
        RecordType(node, target);
        if (CopiesValue(node.WhenTrue, target) || CopiesValue(node.WhenFalse, target)) InvalidateNullableEscapes();
    }
}
