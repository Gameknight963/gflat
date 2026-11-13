namespace gflat.ast;

public sealed class ConditionalExpression(AstNode condition, AstNode whenTrue, AstNode whenFalse, int line) : AstNode(line)
{
    public AstNode Condition { get; } = condition;
    public AstNode WhenTrue { get; } = whenTrue;
    public AstNode WhenFalse { get; } = whenFalse;
    public override void Accept(IVisitor visitor) => visitor.Visit(this);
}
