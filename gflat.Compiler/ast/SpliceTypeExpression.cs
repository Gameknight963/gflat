namespace gflat.ast;

public sealed class SpliceTypeExpression(AstNode reflection, int line) : TypeExpression(line)
{
    public AstNode Reflection { get; } = reflection;
    public override void Accept(IVisitor visitor) => visitor.Visit(this);
}
