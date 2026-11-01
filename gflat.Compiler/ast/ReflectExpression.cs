namespace gflat.ast;

public sealed class ReflectExpression(AstNode target, int line) : AstNode(line)
{
    public AstNode Target { get; } = target;
    public override void Accept(IVisitor visitor) => visitor.Visit(this);
}
