namespace gflat.ast;

/// <summary>Explicitly ends an object's lifetime without releasing its storage.</summary>
public sealed class DestructorCallExpression(AstNode target, TypeExpression targetType, int line) : AstNode(line)
{
    public AstNode Target { get; } = target;
    public TypeExpression TargetType { get; } = targetType;

    public override void Accept(IVisitor visitor) => visitor.Visit(this);
}
