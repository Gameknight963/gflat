namespace gflat.ast;

public sealed class SpliceExpression(AstNode reflection, AstNode? receiver, int line) : AstNode(line)
{
    public AstNode Reflection { get; } = reflection;
    public AstNode? Receiver { get; } = receiver;
    public AstNode? Resolved { get; internal set; }
    public override void Accept(IVisitor visitor) => visitor.Visit(this);
}
