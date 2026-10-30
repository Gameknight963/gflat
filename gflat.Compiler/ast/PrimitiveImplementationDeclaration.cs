namespace gflat.ast;

// An implementation belongs to its enclosing interface, never to an open namespace.
public sealed class PrimitiveImplementationDeclaration(TypeExpression target, TypeExpression? contract,
    List<AstNode> members, int line) : AstNode(line)
{
    public TypeExpression Target { get; } = target;
    public TypeExpression? Contract { get; } = contract;
    public List<AstNode> Members { get; } = members;
    public bool ExposesExtensions { get; set; }
    public override void Accept(IVisitor visitor) => visitor.Visit(this);
}
