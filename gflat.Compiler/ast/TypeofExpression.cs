namespace gflat.ast;

public sealed class TypeofExpression(TypeExpression targetType, int line) : AstNode(line)
{
    public TypeExpression TargetType { get; } = targetType;
    public override void Accept(IVisitor visitor) => visitor.Visit(this);
}
