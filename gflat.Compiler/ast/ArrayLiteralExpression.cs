namespace gflat.ast;

public sealed class ArrayLiteralExpression(List<AstNode> elements, int line) : AstNode(line)
{
    public IReadOnlyList<AstNode> Elements { get; } = elements;
    public override void Accept(IVisitor visitor) => visitor.Visit(this);
}
