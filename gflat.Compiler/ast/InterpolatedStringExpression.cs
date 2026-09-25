namespace gflat.ast;

public class InterpolatedStringExpression : CallExpression
{
    public List<AstNode> Parts { get; }
    public string? Prefix { get; }
    public AstNode? Scope { get; }
    public InterpolatedStringExpression(List<AstNode> parts, int line, string? prefix = null, AstNode? scope = null)
        : base(new IdentifierExpression("$interpolation", line), new(), line)
    { Parts = parts; Prefix = prefix; Scope = scope; }
    public override void Accept(IVisitor visitor) => visitor.Visit(this);
}
