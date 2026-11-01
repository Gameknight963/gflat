namespace gflat.ast;

public sealed class TypeofExpression(AstNode operand, int line) : AstNode(line)
{
    public AstNode Operand { get; } = operand;
    public override void Accept(IVisitor visitor) => visitor.Visit(this);
}
