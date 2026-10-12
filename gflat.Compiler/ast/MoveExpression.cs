namespace gflat.ast;

public sealed class MoveExpression(AstNode operand, int line, AstNode? destination = null) : AstNode(line)
{
    public AstNode Operand { get; } = operand;
    public AstNode? Destination { get; } = destination;
    public override void Accept(IVisitor visitor) => visitor.Visit(this);
}
