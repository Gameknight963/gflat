namespace gflat.ast;

/// <summary>Instance-like members supplied by a namespace, without changing a type's layout.</summary>
public sealed class ExtensionDeclaration(TypeExpression target, List<GenericParameter> genericParameters,
    List<AstNode> members, int line) : AstNode(line)
{
    public TypeExpression Target { get; } = target;
    public List<GenericParameter> GenericParameters { get; } = genericParameters;
    public List<AstNode> Members { get; } = members;
    public override void Accept(IVisitor visitor) => visitor.Visit(this);
}
